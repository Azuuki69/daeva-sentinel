using System.Text.RegularExpressions;

namespace BakarmaCoach.Core;

public static class ScreenText
{
    private static string Normal(string text) => Regex.Replace(text.ToLowerInvariant(), "[^a-z]", "");
    private static readonly Dictionary<string, int> Names = new()
    {
        ["waveofdeath"] = 1800655, ["waterbomb"] = 1800540, ["surgingwrath"] = 1800554,
        ["fusillade"] = 1800530, ["swipe"] = 1800560, ["trample"] = 1800570
    };
    public static bool IsBakarma(string text, float confidence)
    {
        if (confidence < 0.55f) return false;
        string name = Normal(text);
        return name.Contains("transcendentbakarma") || Math.Abs(name.Length - 18) <= 2 && Distance(name, "transcendentbakarma") <= 2;
    }
    public static int? Skill(string text, float confidence)
    {
        if (confidence < 0.55f) return null;
        string name = Normal(text);
        if (Names.TryGetValue(name, out int exact)) return exact;
        // Only a single missing/misread letter in a long complete ability name is tolerated.
        var possible = Names.Where(kv => kv.Key.Length >= 8 && Math.Abs(kv.Key.Length - name.Length) <= 1 && Distance(kv.Key, name) <= 1).ToArray();
        return possible.Length == 1 ? possible[0].Value : null;
    }
    private static int Distance(string a, string b)
    {
        if (a.Length > 100 || b.Length > 100) return 100;
        int[] old = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int[] next = new int[b.Length + 1]; next[0] = i;
            for (int j = 1; j <= b.Length; j++) next[j] = Math.Min(Math.Min(next[j - 1] + 1, old[j] + 1), old[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            old = next;
        }
        return old[^1];
    }
}

public sealed record ScreenDecision(bool BossConfirmed, int? Skill, bool Trigger, long Episode, string Reason);

/// <summary>Two fresh, agreeing frames are required. Persistent text is one episode, not repeated attacks.</summary>
public sealed class ScreenDetector
{
    private int? _candidate, _latched;
    private DateTime _first, _last;
    private int _missing;
    private long _episode;
    public void Reset() { _candidate = _latched = null; _first = _last = default; _missing = 0; }
    public ScreenDecision Observe(string boss, float bossConfidence, string ability, float confidence, DateTime captured, DateTime now)
    {
        if (captured > now || now - captured > TimeSpan.FromSeconds(1.2))
        { Reset(); return new(false, null, false, _episode, "Stale frame discarded"); }
        if (_last != default && captured <= _last) return new(false, null, false, _episode, "Repeated frame discarded");
        if (_last != default && captured - _last > TimeSpan.FromSeconds(1.2)) Reset();
        _last = captured;
        bool knownBoss = ScreenText.IsBakarma(boss, bossConfidence);
        int? skill = knownBoss ? ScreenText.Skill(ability, confidence) : null;
        if (skill is null)
        {
            _candidate = null;
            if (++_missing >= 2) _latched = null;
            return new(knownBoss, null, false, _episode, knownBoss ? "No known cast name visible" : "Waiting for Transcendent Bakarma's name");
        }
        _missing = 0;
        if (_latched == skill) return new(true, skill, false, _episode, "Cast still visible");
        if (_candidate != skill) { _candidate = skill; _first = captured; return new(true, skill, false, _episode, "Confirming cast on the next frame"); }
        if (captured - _first < TimeSpan.FromMilliseconds(120)) return new(true, skill, false, _episode, "Confirming cast on the next frame");
        _latched = skill; _candidate = null; _episode++;
        return new(true, skill, true, _episode, "Cast confirmed by two frames");
    }
}

public sealed class DetectionFusion
{
    private sealed record Network(DateTime Start, DateTime End);
    private sealed record Screen(long Episode, DateTime At);
    private readonly Dictionary<string, Network> _network = [];
    private readonly Dictionary<string, Screen> _screen = [];
    public string LastSource { get; private set; } = "";
    public void Reset() { _network.Clear(); _screen.Clear(); LastSource = ""; }
    public bool NetworkCast(string key, DateTime start, DateTime end)
    {
        _network[key] = new(start, end);
        bool same = _screen.TryGetValue(key, out var image) && Math.Abs((image.At - start).TotalSeconds) <= 1.5;
        LastSource = same ? "BOTH" : "NET";
        return !same;
    }
    public bool ScreenCast(string key, long episode, DateTime at)
    {
        if (_screen.TryGetValue(key, out var previous) && previous.Episode == episode) return false;
        _screen[key] = new(episode, at);
        bool same = _network.TryGetValue(key, out var net) && at >= net.Start.AddMilliseconds(-200) && at <= net.End.AddMilliseconds(500);
        LastSource = same ? "BOTH" : "SCREEN";
        return !same;
    }
}
