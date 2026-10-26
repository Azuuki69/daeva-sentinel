using System.IO;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;
using AionDPS.Aion2.Protocol;
using BakarmaCoach.Core;

namespace BakarmaCoach;

/// <summary>Runs only the upstream passive parser, never its UI, uploads, update checks or account services.</summary>
public sealed class LiveInput : IDisposable
{
    private readonly Aion2PacketCombatSource _source;
    private readonly Encounter _engine;
    private readonly Action<object> _log;
    private DateTime _last;
    private DateTime? _engaged;
    private int _epoch = -1;
    private readonly HashSet<string> _castLog = [];
    private readonly HashSet<string> _staggerLog = [];
    private readonly ThreatEstimate _threat = new();
    private readonly CombatMeter _damage = new();
    private int? _threatBoss;
    private DateTime? _threatEngaged;
    public ThreatView Threat { get; private set; } = ThreatView.Empty;
    public DamageView Damage { get; private set; } = DamageView.Empty;
    public string Status { get; private set; } = "Waiting for Aion 2. Start capture before entering Draupnir.";
    public event Action? Reset;
    public double? Hp { get; private set; }
    public int CastsSeen { get; private set; }
    public bool Failed { get; private set; }

    public LiveInput(Encounter engine, Action<object> log)
    {
        _engine = engine; _log = log; _last = DateTime.Now;
        _source = new Aion2PacketCombatSource(Aion2Protocol.Load(), adapterId: CaptureAdapters.AllAdapters);
        _source.StatusChanged += status =>
        {
            Status = status.Message;
            Failed = status.State == AionDPS.Combat.Sources.SourceState.Error;
        };
    }
    public void Start() => _source.Start();

    public void Tick(DateTime now, bool allowVoice)
    {
        var batch = _source.Poll(false); // Drain bounded queues even while the game is in the background.
        var dir = (Aion2EntityDirectory)_source.Entities;
        if (_epoch != dir.ZoneEpoch)
        {
            _epoch = dir.ZoneEpoch; Clear();
        }
        if (_threatBoss is int previousThreatBoss && _threatEngaged is not null && dir.EngagedSince(previousThreatBoss) != _threatEngaged)
        { _threat.Reset(); _threatBoss = null; _threatEngaged = null; }
        foreach (var hit in batch.Damage)
        {
            if (hit.IsHeal || !dir.IsKnownPlayer(hit.SourceObjectId) || dir.IsKnownPlayer(hit.TargetObjectId)) continue;
            bool inParty = hit.SourceObjectId == dir.LocalPlayerId || dir.NameFor(hit.SourceObjectId) is string name && dir.PartyNames.Contains(name);
            if (inParty && (dir.IsKnownMonster(hit.TargetObjectId) || dir.BossNpcIdOf(hit.TargetObjectId) is not null))
                _damage.Add(hit.SourceObjectId, hit.Amount, hit.Timestamp, now);
            if (!inParty || !Encounter.BossIds.Contains(dir.BossNpcIdOf(hit.TargetObjectId) ?? 0)) continue;
            if (_threatBoss != hit.TargetObjectId) { _threat.Reset(); _threatBoss = hit.TargetObjectId; }
            _threatEngaged = dir.EngagedSince(hit.TargetObjectId);
            _threat.Add(hit.SourceObjectId, hit.Amount);
        }
        if (_threatBoss is null && _engine.Actor is int observedBoss) _threatBoss = observedBoss;
        int? aimed = _threatBoss is int threatBoss ? dir.Combat.TargetOf(threatBoss, now, TimeSpan.FromSeconds(2)) : null;
        if (aimed == _threatBoss || aimed is int target && !dir.IsKnownPlayer(target)) aimed = null;
        Threat = _threat.View(aimed, dir.LocalPlayerId, dir.NameFor);
        Damage = _damage.View(now, dir.LocalPlayerId, dir.NameFor, _threatBoss is int engagedBoss && dir.EngagedSince(engagedBoss) is not null);
        // Reset before processing new windups so the first cast of a fresh attempt is retained.
        if (_engine.Actor is int previousBoss)
        {
            var engagement = dir.EngagedSince(previousBoss);
            if (_engaged is not null && engagement != _engaged) Clear();
            else _engaged = engagement;
        }
        var views = dir.Combat.Casts(now, TimeSpan.Zero);
        var active = new HashSet<string>();
        foreach (var view in views)
        {
            var c = view.Notice;
            int npc = dir.BossNpcIdOf(c.Actor) ?? 0;
            if (!Encounter.BossIds.Contains(npc)) continue;
            var cast = new Cast(c.Actor, npc, c.SkillId, c.Target, c.Start, c.DurationMs);
            active.Add(cast.Key);
            if (_castLog.Add(cast.Key))
            {
                CastsSeen++;
                _log(new { kind = "cast", at = c.Start, actor = c.Actor, npc, skill = c.SkillId, durationMs = c.DurationMs,
                    knownRule = Encounter.Rules.ContainsKey(c.SkillId) });
            }
            if (allowVoice) _engine.OnCast(cast, dir.LocalPlayerId, now);
        }
        _castLog.IntersectWith(active);
        if (_engine.Actor is int boss)
        {
            _engaged = dir.EngagedSince(boss);

            if (_engine.Actor is int currentBoss)
            {
                var sample = dir.HitPoints.Latest(currentBoss);
                // Never estimate an HP percentage from the first partial reading.
                bool knownMax = dir.HitPoints.SpawnMaxOf(currentBoss) is > 0
                    || (Aion2BossCatalog.Find(dir.BossNpcIdOf(currentBoss) ?? 0)?.MaxHp ?? 0) > 0;
                long max = knownMax ? Aion2BossHp.RealMax(dir, currentBoss) ?? 0 : 0;
                Hp = sample is { } h && max > 0 ? Math.Clamp(h.Hp * 100d / max, 0, 100) : null;
                if (sample is { Hp: <= 0 }) Clear(resetDamage: false);
                else if (Hp is double hp && allowVoice)
                {
                    _engine.OnHp(currentBoss, hp, now);
                    if (_engine.Actor is null) { _threat.Reset(); _damage.Reset(); _threatBoss = null; _log(new { kind = "hp-reset", at = now }); Reset?.Invoke(); }
                }
            }
        }

        // Slice returns decoded stagger observations; they never cause an alert outside a detected wave.
        var extras = dir.Combat.Slice(_last.AddMilliseconds(-200), now, _ => null);
        foreach (var s in extras.Staggers)
        {
            if (!Encounter.BossIds.Contains(dir.BossNpcIdOf(s.Entity) ?? 0)) continue;
            string key = $"{s.Entity}:{s.At.Ticks}:{s.Current}:{s.Broke}";
            if (!_staggerLog.Add(key)) continue;
            _log(new { kind = "stagger", at = s.At, actor = s.Entity, current = s.Current, max = s.Max, broken = s.Broke });
            if (allowVoice) _engine.OnStagger(s.Entity, s.Current, s.Max, s.Broke, s.At, now);
        }
        if (_staggerLog.Count > 1000) _staggerLog.Clear();
        _last = now;
        if (!allowVoice) { _engine.Reset(); Reset?.Invoke(); }
        else _engine.Tick(now, _engine.Active is not { } current || active.Contains(current.Key));
    }
    private void Clear(bool resetDamage = true)
    {
        _engine.Reset(); _engaged = null; Hp = null; _castLog.Clear(); _staggerLog.Clear();
        _threat.Reset(); if (resetDamage) _damage.Reset(); else _damage.Finish(DateTime.Now);
        _threatBoss = null; _threatEngaged = null; Threat = ThreatView.Empty; if (resetDamage) Damage = DamageView.Empty; Reset?.Invoke();
    }
    public void ResetMeters() { _damage.Reset(); _threat.Reset(); Damage = DamageView.Empty; Threat = ThreatView.Empty; }
    public void Dispose() { _source.Dispose(); Clear(); }
}

public sealed class EventLog : IDisposable
{
    private readonly string _dir;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private long _count;
    public bool Enabled { get; set; } = true;
    public string? Error { get; private set; }
    public EventLog(string data) { _dir = Path.Combine(data, "logs"); Directory.CreateDirectory(_dir); }
    public void Add(object value)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            try
            {
                if (_writer is null)
                {
                    // Bound local diagnostic storage; logs contain mechanic ids and times, no packets or names.
                    foreach (var old in new DirectoryInfo(_dir).GetFiles("events-*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).Skip(19)) old.Delete();
                    _writer = new StreamWriter(Path.Combine(_dir, $"events-{DateTime.Now:yyyyMMdd-HHmmss-fff}.jsonl")) { AutoFlush = true };
                }
                _writer.WriteLine(JsonSerializer.Serialize(value));
                if (++_count >= 10000) { _writer.Dispose(); _writer = null; _count = 0; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = ex.Message; Enabled = false; }
        }
    }
    public void Dispose() { lock (_gate) _writer?.Dispose(); }
}
