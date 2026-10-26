namespace BakarmaCoach.Core;

public sealed record ThreatRow(int Player, string Name, double Relative, bool IsYou, bool IsTarget);
public sealed record ThreatView(string Target, bool TargetIsYou, IReadOnlyList<ThreatRow> Rows)
{
    public static ThreatView Empty { get; } = new("Unknown", false, []);
}

/// <summary>Damage-only comparison, not server enmity. No made-up healing or tank multipliers.</summary>
public sealed class ThreatEstimate
{
    private readonly Dictionary<int, double> _damage = [];
    public void Reset() => _damage.Clear();
    public void Add(int player, long damage)
    {
        if (player <= 0 || damage <= 0 || _damage.Count >= 100 && !_damage.ContainsKey(player)) return;
        _damage[player] = _damage.GetValueOrDefault(player) + damage;
    }
    public ThreatView View(int? target, int local, Func<int, string?> nameOf)
    {
        double lead = _damage.Count > 0 ? _damage.Values.Max() : 0;
        var rows = _damage.OrderByDescending(kv => kv.Value).Take(3)
            .Select(kv => new ThreatRow(kv.Key, kv.Key == local ? "You" : nameOf(kv.Key) ?? "Player", lead > 0 ? kv.Value / lead * 100 : 0, kv.Key == local, kv.Key == target)).ToArray();
        return new(target is int id ? id == local ? "You" : nameOf(id) ?? "Unknown player" : "Unknown", target == local && local > 0, rows);
    }
}
