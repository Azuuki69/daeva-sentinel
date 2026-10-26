using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BakarmaLauncher.Core;

int passed = 0, failed = 0; var cases = new List<object>();
void Check(string name, Action action) { try { action(); passed++; cases.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; cases.Add(new { name, passed = false, error = ex.Message }); Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Reject(Action action) { try { action(); } catch { return; } throw new Exception("Unsafe input was accepted"); }
using var signer = RSA.Create(2048); string publicKey = signer.ExportSubjectPublicKeyInfoPem();
ReleaseManifest release = new("daeva-sentinel", "0.3.0", "https://github.com/Azuuki69/daeva-sentinel/releases/download/v0.3.0/app.zip", new string('a', 64), 12345);
string Sign(ReleaseManifest value)
{
    byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value);
    return JsonSerializer.Serialize(new SignedEnvelope(Convert.ToBase64String(payload), Convert.ToBase64String(signer.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))));
}
Check("Valid publisher signature is accepted", () => Assert(PackageSecurity.Verify(Sign(release), publicKey).Version == "0.3.0"));
Check("A different publisher key is rejected", () => { using var other = RSA.Create(2048); Reject(() => PackageSecurity.Verify(Sign(release), other.ExportSubjectPublicKeyInfoPem())); });
Check("Tampered signed payload is rejected", () =>
{
    var envelope = JsonSerializer.Deserialize<SignedEnvelope>(Sign(release))!;
    var changed = envelope with { Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")) };
    Reject(() => PackageSecurity.Verify(JsonSerializer.Serialize(changed), publicKey));
});
Check("Unsigned JSON is rejected", () => Reject(() => PackageSecurity.Verify(JsonSerializer.Serialize(release), publicKey)));
Check("Wrong product is rejected", () => Reject(() => PackageSecurity.Verify(Sign(release with { Product = "other" }), publicKey)));
Check("HTTP package URLs are rejected", () => Reject(() => PackageSecurity.Verify(Sign(release with { PackageUrl = release.PackageUrl.Replace("https:", "http:") }), publicKey)));
Check("Unrelated download hosts are rejected", () => Reject(() => PackageSecurity.Verify(Sign(release with { PackageUrl = "https://example.com/app.zip" }), publicKey)));
Check("Other GitHub repositories are rejected", () => Reject(() => PackageSecurity.Verify(Sign(release with { PackageUrl = "https://github.com/someone/other/releases/download/v1/app.zip" }), publicKey)));
Check("Oversized package declarations are rejected", () => Reject(() => PackageSecurity.Verify(Sign(release with { Size = 900_000_000 }), publicKey)));
Check("Invalid version paths are rejected", () => Reject(() => PackageSecurity.Verify(Sign(release with { Version = "../other" }), publicKey)));
Check("Invalid checksum format is rejected", () => Reject(() => PackageSecurity.Verify(Sign(release with { Sha256 = "bad" }), publicKey)));
string temporary = Path.Combine(Path.GetTempPath(), "bakarma-updater-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
string MakeZip(string name, string entryName)
{
    string path = Path.Combine(temporary, name + ".zip"); using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    using var writer = new StreamWriter(zip.CreateEntry(entryName).Open()); writer.Write("synthetic test fixture"); return path;
}
Check("Correct file checksum is accepted", () =>
{
    string path = Path.Combine(temporary, "hash.txt"); File.WriteAllText(path, "test"); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); Assert(PackageSecurity.HashMatches(path, hash));
});
Check("Changed download is rejected by checksum", () => { string file = Path.Combine(temporary, "changed.txt"); File.WriteAllText(file, "changed"); Assert(!PackageSecurity.HashMatches(file, new string('0', 64))); });
Check("Valid application archive extracts", () =>
{
    string path = MakeZip("valid", "Daeva Sentinel.exe"), target = Path.Combine(temporary, "valid"); Directory.CreateDirectory(target); PackageSecurity.Extract(path, target); Assert(File.Exists(Path.Combine(target, "Daeva Sentinel.exe")));
});
Check("Zip traversal cannot write outside staging", () =>
{
    string path = MakeZip("traversal", "../escaped.exe"), target = Path.Combine(temporary, "traversal"); Directory.CreateDirectory(target); Reject(() => PackageSecurity.Extract(path, target)); Assert(!File.Exists(Path.Combine(temporary, "escaped.exe")));
});
Check("Alternate data streams are rejected", () => { string path = MakeZip("stream", "file.txt:evil"), target = Path.Combine(temporary, "stream"); Directory.CreateDirectory(target); Reject(() => PackageSecurity.Extract(path, target)); });
Check("Archive without application executable is rejected", () => { string path = MakeZip("missing", "readme.txt"), target = Path.Combine(temporary, "missing"); Directory.CreateDirectory(target); Reject(() => PackageSecurity.Extract(path, target)); });
int liveIndex = Array.IndexOf(args, "--live-update");
if (liveIndex >= 0 && liveIndex + 1 < args.Length) Check("Live GitHub signed update installs atomically, preserves settings and rolls back", () =>
{
    string keyPath = Path.GetFullPath(args[liveIndex + 1]);
    string root = Path.Combine(temporary, "live-install"); Directory.CreateDirectory(root);
    File.Copy(keyPath, Path.Combine(root, "update-public-key.pem"));
    File.Copy(Path.Combine(Path.GetDirectoryName(keyPath)!, "Daeva Sentinel.dll"), Path.Combine(root, "Daeva Sentinel.dll"));
    string baseline = Path.Combine(root, "versions", "0.0.0"); Directory.CreateDirectory(baseline);
    File.WriteAllText(Path.Combine(baseline, "Daeva Sentinel.exe"), "Synthetic baseline fixture; never executed.");
    File.WriteAllText(Path.Combine(root, "launcher-state.json"), JsonSerializer.Serialize(new InstalledState("0.0.0", null)));
    string data = Path.Combine(root, "data"); Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "settings.json"), "preserve-me");
    using var client = new UpdateClient(root);
    var update = client.Check(CancellationToken.None).GetAwaiter().GetResult();
    client.Install(update, new Progress<double>(), _ => { }, CancellationToken.None).GetAwaiter().GetResult();
    Assert(client.InstalledVersion == update.Version && File.Exists(client.AppPath));
    Assert(File.ReadAllText(Path.Combine(data, "settings.json")) == "preserve-me");
    client.Rollback(); Assert(client.InstalledVersion == "0.0.0");
});
Console.WriteLine($"{passed} passed, {failed} failed");
if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(new { passed, failed, cases }, new JsonSerializerOptions { WriteIndented = true }));
return failed == 0 ? 0 : 1;
