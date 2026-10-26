namespace BakarmaCoach.Core;

public enum Difficulty { Exploration, Conquest, ConquestHard }
public sealed record Warning(string Key, string Text, int Priority, DateTime At, DateTime Expires, string Source = "NET");
public sealed record Cast(int Actor, int NpcId, int Skill, int Target, DateTime Start, int DurationMs)
{
    public DateTime End => Start.AddMilliseconds(DurationMs);
    public string Key => $"{Actor}:{Skill}:{Start.Ticks}";
}
public sealed record Mechanic(string Label, string Call, int Priority, bool Wave = false, bool Personal = false);

/// <summary>Pure event-driven encounter rules. Difficulty never fabricates a rotation or a jump timer.</summary>
public sealed class Encounter
{
    public static readonly HashSet<int> BossIds = [2300470, 2300471, 2310470, 2310471, 2320470, 2320471, 2330423, 2330424];
    public static readonly Dictionary<int, Mechanic> Rules = new()
    {
        [1800640] = new("Wave of Death", "Waves incoming. Move toward Bakarma and jump.", 100, true),
        [1800655] = new("Wave of Death", "Waves incoming. Move toward Bakarma and jump.", 100, true),
        [1800656] = new("Wave of Death", "Waves incoming. Move toward Bakarma and jump.", 100, true),
        [1800520] = new("Frontal attack", "Frontal attack. Watch the boss.", 40),
        [1800560] = new("Swipe", "Frontal sweep. Get behind Bakarma.", 60),
        [1800570] = new("Trample", "Frontal charge. Move out of the line.", 60),
        [1800530] = new("Fusillade", "Projectiles incoming. Watch the ground.", 40),
        [1800531] = new("Fusillade", "Projectiles incoming. Watch the ground.", 40),
        [1800532] = new("Fusillade", "Projectiles incoming. Watch the ground.", 40),
        [1800540] = new("Water Bomb", "Water bomb on you. Keep moving.", 70, Personal: true),
        [1800541] = new("Water Bomb", "Water bomb on you. Keep moving.", 70, Personal: true),
        [1800542] = new("Water Bomb", "Water bomb on you. Keep moving.", 70, Personal: true),
        [1800544] = new("Water Bomb", "Water bomb on you. Keep moving.", 70, Personal: true),
        [1800554] = new("Surging Wrath", "Area attack. Watch the ground.", 60),
        [1800555] = new("Surging Wrath", "Area attack. Watch the ground.", 60),
        [1800556] = new("Surging Wrath", "Area attack. Watch the ground.", 60),
        [1800557] = new("Surging Wrath", "Area attack. Watch the ground.", 60),
        [1800558] = new("Surging Wrath", "Area attack. Watch the ground.", 60),
        [1800559] = new("Surging Wrath", "Area attack. Watch the ground.", 60)
    };
    public Difficulty Mode { get; set; } = Difficulty.Conquest;
    public bool HpReminders { get; set; }
    public double[] WaveHp { get; set; } = [75, 25];
    public bool Countdown { get; set; }
    public int? Actor { get; private set; }
    public Cast? Active { get; private set; }
    public bool StaggerOpen { get; private set; }
    public event Action<Warning>? Alert;
    private readonly DetectionFusion _fusion = new();
    public string LastSource => _fusion.LastSource;
    private readonly HashSet<string> _seen = [];
    private readonly Dictionary<string, DateTime> _spoken = [];
    private readonly HashSet<int> _counted = [];
    private readonly HashSet<double> _prepared = [];
    private double? _hp;
    private DateTime? _waveUntil;
    private DateTime? _waveStart;
    private bool _staggerDone;

    public void Reset()
    {
        Actor = null; Active = null; StaggerOpen = false; _waveUntil = null; _waveStart = null;
        _staggerDone = false; _hp = null; _seen.Clear(); _spoken.Clear(); _counted.Clear(); _prepared.Clear(); _fusion.Reset();
    }

    public bool OnCast(Cast cast, int localPlayer, DateTime now)
    {
        if (!BossIds.Contains(cast.NpcId) || !Rules.TryGetValue(cast.Skill, out var rule)
            || cast.DurationMs is < 50 or > 120000 || now < cast.Start || now >= cast.End
            || now - cast.Start > TimeSpan.FromSeconds(1.5) || !_seen.Add(cast.Key)) return false;
        if (Actor is int actor && actor != cast.Actor) return false;
        Actor = cast.Actor;
        if (_seen.Count > 2000) { _seen.Clear(); _seen.Add(cast.Key); }
        if (Active is null || rule.Priority >= Rules[Active.Skill].Priority || now >= Active.End)
        {
            Active = cast; _counted.Clear();
        }
        if (rule.Wave)
        {
            if (_waveUntil is null || now >= _waveUntil)
            {
                StaggerOpen = false; _staggerDone = false;
                _waveStart = cast.Start;
            }
            _waveUntil = cast.End;
        }
        string text = rule.Personal && (localPlayer <= 0 || cast.Target != localPlayer)
            ? "Water bombs incoming. Watch the ground." : rule.Call;
        if (_fusion.NetworkCast(rule.Label, cast.Start, cast.End))
            Emit(rule.Label, text, rule.Priority, now, Math.Min(6, (cast.End - now).TotalSeconds), _fusion.LastSource, cooldown: false);
        return true;
    }

    public void OnScreenCast(int skill, long episode, DateTime captured, DateTime now)
    {
        if (captured > now || now - captured > TimeSpan.FromSeconds(1.2) || !Rules.TryGetValue(skill, out var rule)) return;
        if (!_fusion.ScreenCast(rule.Label, episode, captured)) return;
        // Screen text cannot establish who is targeted, cast duration, HP or an open stagger gauge.
        string text = rule.Personal ? "Water bombs incoming. Watch the ground." : rule.Call;
        Emit(rule.Label, text, rule.Priority, now, 5, "SCREEN", cooldown: false);
    }

    public void OnHp(int actor, double hp, DateTime now)
    {
        if (actor != Actor || double.IsNaN(hp) || hp < 0 || hp > 100) return;
        if (hp == 0) { Reset(); return; }
        if (_hp is double old && hp > old + 10 && hp >= 95) { Reset(); return; }
        if (HpReminders && _hp is double previous && previous >= hp)
            foreach (double threshold in WaveHp.Where(x => x > 0 && x < 95))
            {
                double prepareAt = threshold + 5;
                if (previous > prepareAt && hp <= prepareAt && hp > threshold && _prepared.Add(threshold))
                    Emit("prepare", "Wave phase soon. Save your stagger skills.", 30, now, 5);
            }
        _hp = hp;
    }

    public void OnStagger(int actor, int current, int max, bool broken, DateTime at, DateTime now)
    {
        // The bar can exist between checks; only announce its reading during a detected wave cast.
        if (actor != Actor || _waveUntil is null || now >= _waveUntil || at < _waveStart || at > now || now - at > TimeSpan.FromSeconds(1)) return;
        if (broken)
        {
            if (!_staggerDone)
            {
                _staggerDone = true; StaggerOpen = false; Active = null;
                Emit("stagger-broken", "Stagger broken. Attack Bakarma.", 100, now, 5);
            }
            return;
        }
        if (!_staggerDone && !StaggerOpen && max > 0 && current > 0 && current <= max)
        {
            StaggerOpen = true;
            Emit("stagger-open", "Stagger now. Use your stagger skills.", 110, now, 5);
        }
    }

    public void Tick(DateTime now, bool castStillRunning = true)
    {
        if (_waveUntil is DateTime waveEnd && now >= waveEnd) { _waveUntil = null; StaggerOpen = false; }
        if (Active is not { } cast) return;
        if (!castStillRunning || now >= cast.End)
        {
            if (!castStillRunning && Rules[cast.Skill].Wave) { _waveUntil = null; StaggerOpen = false; }
            Active = null; return;
        }
        double remaining = (cast.End - now).TotalSeconds;
        int second = (int)Math.Ceiling(remaining);
        // This counts CAST END only, never a predicted impact or jump. Disabled by default.
        if (Countdown && cast.DurationMs >= 5000 && second is >= 1 and <= 3 && _counted.Add(second))
            Emit("count-" + second, second.ToString(), 20, now, Math.Min(remaining, 0.8));
    }

    private void Emit(string key, string text, int priority, DateTime now, double lifetime, string source = "NET", bool cooldown = true)
    {
        if (cooldown && _spoken.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(2.5)) return;
        _spoken[key] = now;
        Alert?.Invoke(new Warning(key, text, priority, now, now.AddSeconds(Math.Max(0.1, lifetime)), source));
    }
}
