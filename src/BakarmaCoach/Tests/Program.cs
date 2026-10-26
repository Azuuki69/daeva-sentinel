using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using BakarmaCoach.Core;

var results = new List<object>(); int passed = 0, failed = 0;
void Check(string name, Action test)
{
    try { test(); passed++; results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; results.Add(new { name, passed = false, error = ex.Message }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
DateTime now = new(2026, 10, 26, 12, 0, 0);
Cast Wave(DateTime? at = null) => new(42, 2300471, 1800655, 42, at ?? now, 18000);
(Encounter e, List<Warning> warnings) Fresh()
{
    var e = new Encounter(); var warnings = new List<Warning>(); e.Alert += warnings.Add; return (e, warnings);
}
foreach (var mode in Enum.GetValues<Difficulty>()) Check(mode + " reacts to wave cast", () =>
{
    var (e, w) = Fresh(); e.Mode = mode; e.OnCast(Wave(), 1, now); Assert(w.Count == 1 && w[0].Text.Contains("jump"));
});
Check("Conquest is the default", () => Assert(new Encounter().Mode == Difficulty.Conquest));
Check("HP speculation disabled by default", () => Assert(!new Encounter().HpReminders));
Check("Countdown disabled by default", () => Assert(!new Encounter().Countdown));
Check("Repeated poll does not repeat speech", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnCast(Wave(), 1, now.AddMilliseconds(100)); Assert(w.Count == 1); });
Check("Different monster cannot trigger a warning", () => { var (e, w) = Fresh(); e.OnCast(Wave() with { NpcId = 123 }, 1, now); Assert(w.Count == 0); });
Check("Unknown skill is silent", () => { var (e, w) = Fresh(); e.OnCast(Wave() with { Skill = 999 }, 1, now); Assert(w.Count == 0); });
Check("Stale cast does not announce on focus return", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now.AddSeconds(3)); Assert(w.Count == 0); });
Check("Expired cast is silent", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now.AddSeconds(18)); Assert(w.Count == 0); });
Check("Future cast is rejected", () => { var (e, w) = Fresh(); e.OnCast(Wave(now.AddSeconds(1)), 1, now); Assert(w.Count == 0); });
Check("Invalid duration is rejected", () => { var (e, w) = Fresh(); e.OnCast(Wave() with { DurationMs = 0 }, 1, now); Assert(w.Count == 0); });
Check("Other actor cannot replace current boss", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnCast(Wave() with { Actor = 99 }, 1, now); Assert(w.Count == 1 && e.Actor == 42); });
Check("Stagger is not announced on cast alone", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); Assert(!e.StaggerOpen && w.Count == 1); });
Check("Fresh stagger reading announces once", () =>
{
    var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnStagger(42, 1200, 1200, false, now, now);
    e.OnStagger(42, 1000, 1200, false, now.AddMilliseconds(100), now.AddMilliseconds(100)); Assert(e.StaggerOpen && w.Count(x => x.Key == "stagger-open") == 1);
});
Check("Stagger outside wave is silent", () => { var (e, w) = Fresh(); e.OnStagger(42, 1200, 1200, false, now, now); Assert(w.Count == 0); });
Check("Other boss stagger is silent", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnStagger(9, 1200, 1200, false, now, now); Assert(!e.StaggerOpen); });
Check("Stale stagger reading is silent", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnStagger(42, 1200, 1200, false, now.AddSeconds(-2), now); Assert(!e.StaggerOpen); });
Check("Pre-wave stagger readings do not open the new window", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnStagger(42, 1200, 1200, false, now.AddMilliseconds(-100), now); Assert(!e.StaggerOpen); });
Check("Broken stagger cancels cast countdown", () =>
{
    var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnStagger(42, 0, 0, true, now, now); Assert(e.Active is null && !e.StaggerOpen && w.Any(x => x.Key == "stagger-broken"));
});
Check("Stagger cannot reopen after a break in the same wave", () =>
{
    var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnStagger(42, 0, 0, true, now, now); e.OnStagger(42, 1200, 1200, false, now, now); Assert(!e.StaggerOpen);
});
Check("Reset permits warnings on the next pull", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.Reset(); e.OnCast(Wave(), 1, now); Assert(w.Count == 2); });
Check("Death clears all active state", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnHp(42, 0, now); Assert(e.Actor is null && e.Active is null); });
Check("HP return to full resets the attempt", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnHp(42, 40, now); e.OnHp(42, 100, now); Assert(e.Actor is null); });
Check("HP reminders require opting in", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnHp(42, 90, now); e.OnHp(42, 79, now); Assert(!w.Any(x => x.Key == "prepare")); });
Check("Opt-in reminder uses a real threshold crossing", () =>
{
    var (e, w) = Fresh(); e.HpReminders = true; e.OnCast(Wave(), 1, now); e.OnHp(42, 90, now); e.OnHp(42, 79, now); Assert(w.Any(x => x.Key == "prepare"));
});
Check("Joining midfight does not invent preparation", () => { var (e, w) = Fresh(); e.HpReminders = true; e.OnCast(Wave(), 1, now); e.OnHp(42, 79, now); Assert(!w.Any(x => x.Key == "prepare")); });
Check("HP jump past mechanic does not announce it as upcoming", () => { var (e, w) = Fresh(); e.HpReminders = true; e.OnCast(Wave(), 1, now); e.OnHp(42, 90, now); e.OnHp(42, 60, now); Assert(!w.Any(x => x.Key == "prepare")); });
Check("Personal warning requires own-player identity", () =>
{
    var (e, w) = Fresh(); e.OnCast(Wave() with { Skill = 1800540, Target = 99, DurationMs = 900 }, 1, now); Assert(!w[0].Text.Contains("on you"));
});
Check("Own targeted bomb gets personal warning", () =>
{
    var (e, w) = Fresh(); e.OnCast(Wave() with { Skill = 1800540, Target = 1, DurationMs = 900 }, 1, now); Assert(w[0].Text.Contains("on you"));
});
Check("Cast cancellation clears the timer", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.Tick(now.AddSeconds(1), false); Assert(e.Active is null); });
Check("Cancelled wave cannot trigger a later stagger alert", () =>
{
    var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.Tick(now.AddMilliseconds(100), false);
    e.OnStagger(42, 1200, 1200, false, now.AddSeconds(1), now.AddSeconds(1)); Assert(!e.StaggerOpen && w.Count == 1);
});
Check("Cast countdown announces each second once", () =>
{
    var (e, w) = Fresh(); e.Countdown = true; e.OnCast(Wave(), 1, now);
    foreach (int ms in new[] { 15100, 15200, 16100, 16200, 17100, 17200 }) e.Tick(now.AddMilliseconds(ms));
    Assert(w.Where(x => x.Key.StartsWith("count-")).Select(x => x.Text).SequenceEqual(new[] { "3", "2", "1" }));
});
Check("Short casts do not get a misleading countdown", () =>
{
    var (e, w) = Fresh(); e.Countdown = true; e.OnCast(Wave() with { DurationMs = 1500 }, 1, now); e.Tick(now.AddMilliseconds(100)); Assert(w.Count == 1);
});
Check("Wave state expires without a guessed success", () => { var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.Tick(now.AddSeconds(19)); Assert(e.Active is null && !e.StaggerOpen && w.Count == 1); });

// Feed actual protocol-shaped bytes to the downloaded decoder, then into the new encounter rules.
Check("Real decoder windup -> Bakarma wave warning", () =>
{
    var dir = new Aion2EntityDirectory();
    var decoder = new Aion2FrameDecoder(Aion2Protocol.Load(), dir);
    using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
    writer.Write(new byte[] { 0x09, 0x38, 42, 0 }); writer.Write(1800655); writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)42); writer.Write(0f);
    int value = 18000; while (value >= 128) { writer.Write((byte)((value & 127) | 128)); value >>= 7; } writer.Write((byte)value);
    decoder.Decode(bytes.ToArray(), now);
    var casts = dir.Combat.Casts(now, TimeSpan.Zero); Assert(casts.Count == 1, "Windup not decoded");
    var cast = casts[0].Notice; Assert(cast.DurationMs == 18000 && cast.SkillId == 1800655);
    var (e, w) = Fresh(); e.OnCast(new(cast.Actor, 2300471, cast.SkillId, cast.Target, cast.Start, cast.DurationMs), 1, now); Assert(w.Count == 1);
});
Check("Real decoder stagger -> stagger voice warning", () =>
{
    var dir = new Aion2EntityDirectory(); var decoder = new Aion2FrameDecoder(Aion2Protocol.Load(), dir);
    using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
    writer.Write(new byte[] { 0x05, 0xe0, 42, 3, 1 }); writer.Write((uint)1200); writer.Write((uint)1100); writer.Write((byte)0);
    decoder.Decode(bytes.ToArray(), now);
    var stagger = dir.Combat.Slice(now, now, _ => null).Staggers.Single();
    var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnStagger(stagger.Entity, stagger.Current, stagger.Max, stagger.Broke, stagger.At, now); Assert(e.StaggerOpen);
});
Check("OCR only recognizes Transcendent Bakarma", () => { Assert(ScreenText.IsBakarma("Transcendent Bakarma", .95f)); Assert(!ScreenText.IsBakarma("Afflicted Bakarma", .95f)); });
Check("Low-confidence boss name is rejected", () => Assert(!ScreenText.IsBakarma("Transcendent Bakarma", .3f)));
Check("OCR allows one letter error in a long ability name", () => Assert(ScreenText.Skill("Wave of Deatn", .9f) == 1800655));
Check("Short ability names require exact spelling", () => Assert(ScreenText.Skill("Swlpe", .9f) is null));
Check("Tooltip-like sentences cannot trigger casts", () => Assert(ScreenText.Skill("How to avoid Wave of Death", .95f) is null));
Check("Low-confidence ability is rejected", () => Assert(ScreenText.Skill("Wave of Death", .4f) is null));
Check("Screen warning requires two agreeing frames", () =>
{
    var d = new ScreenDetector(); var first = d.Observe("Transcendent Bakarma", .9f, "Wave of Death", .9f, now, now);
    var second = d.Observe("Transcendent Bakarma", .9f, "Wave of Death", .9f, now.AddMilliseconds(300), now.AddMilliseconds(300));
    Assert(!first.Trigger && second.Trigger);
});
Check("Persistent text is not repeated", () =>
{
    var d = new ScreenDetector(); var reads = Enumerable.Range(0, 15).Select(i => d.Observe("Transcendent Bakarma", .9f, "Wave of Death", .9f, now.AddMilliseconds(i * 300), now.AddMilliseconds(i * 300))).ToArray();
    Assert(reads.Count(x => x.Trigger) == 1);
});
Check("Old screen frame cannot trigger", () => { var d = new ScreenDetector(); Assert(!d.Observe("Transcendent Bakarma", .9f, "Wave of Death", .9f, now, now.AddSeconds(2)).Trigger); });
Check("Wrong boss prevents screen warnings", () =>
{
    var d = new ScreenDetector(); d.Observe("Afflicted Bakarma", .95f, "Wave of Death", .95f, now, now);
    Assert(!d.Observe("Afflicted Bakarma", .95f, "Wave of Death", .95f, now.AddMilliseconds(300), now.AddMilliseconds(300)).Trigger);
});
Check("Blank frames rearm later casts", () =>
{
    var d = new ScreenDetector(); int triggers = 0;
    string[] frames = ["Swipe", "Swipe", "", "", "Swipe", "Swipe"];
    for (int i = 0; i < frames.Length; i++) if (d.Observe("Transcendent Bakarma", .9f, frames[i], .9f, now.AddMilliseconds(i * 300), now.AddMilliseconds(i * 300)).Trigger) triggers++;
    Assert(triggers == 2);
});
Check("Network then screen gives one callout", () =>
{
    var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnScreenCast(1800655, 1, now.AddMilliseconds(600), now.AddMilliseconds(600));
    Assert(w.Count == 1 && e.LastSource == "BOTH" && e.Active is not null);
});
Check("Screen then network gives one callout and precise timer", () =>
{
    var (e, w) = Fresh(); e.OnScreenCast(1800655, 1, now, now); e.OnCast(Wave(), 1, now.AddMilliseconds(600));
    Assert(w.Count == 1 && e.LastSource == "BOTH" && e.Active is not null);
});
Check("Screen alone never invents cast timing or targeting", () =>
{
    var (e, w) = Fresh(); e.OnScreenCast(1800540, 1, now, now); Assert(w.Count == 1 && e.Active is null && !w[0].Text.Contains("on you") && w[0].Source == "SCREEN");
});
Check("Later network cast is not suppressed by an old screen episode", () =>
{
    var (e, w) = Fresh(); e.OnScreenCast(1800655, 1, now, now); e.OnCast(Wave(now.AddSeconds(20)), 1, now.AddSeconds(20)); Assert(w.Count == 2);
});
Check("Different mechanics are never fused", () =>
{
    var (e, w) = Fresh(); e.OnCast(Wave(), 1, now); e.OnScreenCast(1800560, 1, now.AddMilliseconds(300), now.AddMilliseconds(300)); Assert(w.Count == 2);
});
Check("DPS uses recorded damage and elapsed time", () =>
{
    var m = new CombatMeter(); m.Add(1, 10000, now, now); var view = m.View(now.AddSeconds(10), 1, _ => "Player", true); Assert(view.Rows[0].Dps == 1000 && view.Total == 10000);
});
Check("DPS shares total 100 percent", () =>
{
    var m = new CombatMeter(); m.Add(1, 7000, now, now); m.Add(2, 3000, now, now); var view = m.View(now.AddSeconds(10), 1, _ => "Player", true); Assert(view.Rows.Sum(x => x.Share) == 100 && view.Rows[0].Share == 70);
});
Check("Meter reset starts a fresh fight", () =>
{
    var m = new CombatMeter(); m.Add(1, 1000, now, now); m.Reset(); Assert(m.View(now, 1, _ => "Player", true).Rows.Count == 0);
});
Check("Completed fight freezes DPS duration", () =>
{
    var m = new CombatMeter(); m.Add(1, 10000, now, now); m.Finish(now.AddSeconds(10)); Assert(m.View(now.AddSeconds(50), 1, _ => "Player", false).Rows[0].Dps == 1000);
});
Check("Damage proxy does not override the observed boss target", () =>
{
    var t = new ThreatEstimate(); t.Add(1, 10000); t.Add(2, 1000); var v = t.View(2, 1, id => id == 2 ? "Tank" : "You"); Assert(v.Rows[0].Relative == 100 && v.Target == "Tank" && !v.TargetIsYou);
});
Check("Threat view can show target without damage data", () => Assert(new ThreatEstimate().View(1, 1, _ => "Player").TargetIsYou));
Console.WriteLine($"{passed} passed, {failed} failed");
if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(new { passed, failed, tests = results }, new JsonSerializerOptions { WriteIndented = true }));
return failed == 0 ? 0 : 1;
