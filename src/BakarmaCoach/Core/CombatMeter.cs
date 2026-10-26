namespace BakarmaCoach.Core;

public sealed record DamageRow(int Player, string Name, double Dps, double Total, double Share, bool IsYou);
public sealed record DamageView(double Seconds, double Total, bool Active, IReadOnlyList<DamageRow> Rows)
{
    public static DamageView Empty { get; } = new(0, 0, false, []);
}

/// <summary>Recorded party damage / encounter elapsed time. No threat assumptions enter the DPS calculation.</summary>
public sealed class CombatMeter
{
    private readonly Dictionary<int, double> _totals = [];
    private DateTime? _start, _last, _end;
    public void Reset() { _totals.Clear(); _start = _last = _end = null; }
    public void Finish(DateTime at) { if (_start is not null) _end = at; }
    public void Add(int player, long damage, DateTime at, DateTime now)
    {
        if (player <= 0 || damage <= 0 || at > now.AddSeconds(1) || now - at > TimeSpan.FromSeconds(5)) return;
        if (_last is DateTime previous && at - previous > TimeSpan.FromSeconds(30)) Reset();
        if (_end is not null) Reset();
        _start = _start is null || at < _start ? at : _start;
        _last = _last is null || at > _last ? at : _last;
        _totals[player] = _totals.GetValueOrDefault(player) + damage;
    }
    public DamageView View(DateTime now, int local, Func<int, string?> names, bool bossEngaged)
    {
        if (_start is null || _last is null) return DamageView.Empty;
        if (!bossEngaged && now - _last > TimeSpan.FromSeconds(30)) _end ??= _last;
        double seconds = Math.Max(1, ((_end ?? now) - _start.Value).TotalSeconds);
        double total = _totals.Values.Sum();
        return new(seconds, total, _end is null, _totals.OrderByDescending(x => x.Value).Take(5)
            .Select(x => new DamageRow(x.Key, x.Key == local ? "You" : names(x.Key) ?? "Player", x.Value / seconds, x.Value, total > 0 ? x.Value / total * 100 : 0, x.Key == local)).ToArray());
    }
}
