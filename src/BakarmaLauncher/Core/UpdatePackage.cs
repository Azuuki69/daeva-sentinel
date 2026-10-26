using System.IO.Compression;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BakarmaLauncher.Core;

public sealed record ReleaseManifest(string Product, string Version, string PackageUrl, string Sha256, long Size);
public sealed record SignedEnvelope(string Payload, string Signature);
public sealed record InstalledState(string Version, string? Previous);

public static class PackageSecurity
{
    public static ReleaseManifest Verify(string json, string publicKey)
    {
        if (json.Length > 65536) throw new InvalidDataException("Update manifest is too large.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var envelope = JsonSerializer.Deserialize<SignedEnvelope>(json, options) ?? throw new InvalidDataException("Missing signed manifest.");
        byte[] payload = Convert.FromBase64String(envelope.Payload), signature = Convert.FromBase64String(envelope.Signature);
        if (payload.Length > 16384) throw new InvalidDataException("Invalid manifest payload.");
        using var rsa = RSA.Create(); rsa.ImportFromPem(publicKey);
        if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) throw new InvalidDataException("Update signature does not match this publisher.");
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(payload, options) ?? throw new InvalidDataException("Missing release details.");
        if (manifest.Product != "daeva-sentinel" || !System.Version.TryParse(manifest.Version, out _) || manifest.Version.Contains('/') || manifest.Version.Contains('\\')
            || manifest.Size is < 1000 or > 500_000_000 || manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid release metadata.");
        var url = new Uri(manifest.PackageUrl);
        if (url.Scheme != "https" || url.Host != "github.com" || !url.AbsolutePath.StartsWith("/Azuuki69/daeva-sentinel/releases/download/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unexpected package download source.");
        return manifest;
    }
    public static bool HashMatches(string file, string expected)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
    public static void Extract(string zipFile, string directory, CancellationToken token = default)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipFile); long expanded = 0;
        if (archive.Entries.Count > 15000) throw new InvalidDataException("Update archive contains too many files.");
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.FullName.Contains(':') || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Links and alternate streams are not allowed.");
            string destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update archive contains an unsafe file path.");
            expanded += entry.Length;
            if (expanded > 1_500_000_000) throw new InvalidDataException("Expanded update exceeds the size limit.");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); entry.ExtractToFile(destination, overwrite: false);
        }
        if (!File.Exists(Path.Combine(root, "Daeva Sentinel.exe"))) throw new InvalidDataException("Update does not contain Daeva Sentinel.exe.");
    }
}

public sealed class UpdateClient : IDisposable
{
    public const string Repository = "https://github.com/Azuuki69/daeva-sentinel";
    public const string Feed = Repository + "/releases/latest/download/update-manifest.json";
    private readonly string _root;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
    public UpdateClient(string root) { _root = Path.GetFullPath(root); _http.DefaultRequestHeaders.UserAgent.ParseAdd("BakarmaLauncher/1.0"); }
    private string StatePath => Path.Combine(_root, "launcher-state.json");
    private string VersionDirectory(string version)
    {
        if (!Version.TryParse(version, out _)) throw new InvalidDataException("Invalid installed version.");
        return Path.Combine(_root, "versions", version);
    }
    public InstalledState? State()
    {
        try { return JsonSerializer.Deserialize<InstalledState>(File.ReadAllText(StatePath)); } catch { return null; }
    }
    public string InstalledVersion => State() is { } state && File.Exists(Path.Combine(VersionDirectory(state.Version), "Daeva Sentinel.exe")) ? state.Version
        : System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(_root, "Daeva Sentinel.dll")).Version?.ToString(3) ?? "0.0.0";
    public string AppPath => State() is { } state && File.Exists(Path.Combine(VersionDirectory(state.Version), "Daeva Sentinel.exe"))
        ? Path.Combine(VersionDirectory(state.Version), "Daeva Sentinel.exe") : Path.Combine(_root, "Daeva Sentinel.exe");
    public string SharedData => Path.Combine(_root, "data");
    private async Task<HttpResponseMessage> Get(Uri uri, CancellationToken token)
    {
        for (int redirects = 0; redirects <= 8; redirects++)
        {
            if (uri.Scheme != "https") throw new InvalidDataException("Updates require HTTPS.");
            var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location); response.Dispose(); uri = next; continue;
            }
            response.EnsureSuccessStatusCode(); return response;
        }
        throw new HttpRequestException("Too many update redirects.");
    }
    public async Task<ReleaseManifest> Check(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20)); token = timeout.Token;
        using var response = await Get(new Uri(Feed), token);
        if (response.Content.Headers.ContentLength is > 65536) throw new InvalidDataException("Manifest is too large.");
        using var stream = await response.Content.ReadAsStreamAsync(token); using var memory = new MemoryStream();
        var buffer = new byte[4096]; int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (memory.Length + read > 65536) throw new InvalidDataException("Manifest is too large.");
            memory.Write(buffer, 0, read);
        }
        return PackageSecurity.Verify(Encoding.UTF8.GetString(memory.ToArray()), File.ReadAllText(Path.Combine(_root, "update-public-key.pem")));
    }
    public async Task Install(ReleaseManifest manifest, IProgress<double> progress, Action<string> status, CancellationToken token)
    {
        if (Version.Parse(manifest.Version) <= Version.Parse(InstalledVersion)) return;
        string cache = Path.Combine(_root, "update-cache"); Directory.CreateDirectory(cache);
        string downloaded = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".zip");
        status("Downloading update…");
        using (var response = await Get(new Uri(manifest.PackageUrl), token))
        using (var input = await response.Content.ReadAsStreamAsync(token))
        using (var output = File.Create(downloaded))
        {
            byte[] buffer = new byte[131072]; long bytes = 0; int read;
            while ((read = await input.ReadAsync(buffer, token)) > 0)
            {
                bytes += read; if (bytes > manifest.Size) throw new InvalidDataException("Downloaded update exceeds its signed size.");
                await output.WriteAsync(buffer.AsMemory(0, read), token); progress.Report(bytes * 100d / manifest.Size);
            }
            if (bytes != manifest.Size) throw new InvalidDataException("Update download is incomplete.");
        }
        status("Verifying package…");
        if (!await Task.Run(() => PackageSecurity.HashMatches(downloaded, manifest.Sha256), token)) throw new InvalidDataException("Update checksum mismatch. Installation was cancelled.");
        string staging = Path.Combine(cache, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        status("Installing update…");
        await Task.Run(() => PackageSecurity.Extract(downloaded, staging, token), token);
        string installed = VersionDirectory(manifest.Version); Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
        if (Directory.Exists(installed)) throw new IOException("This version already exists; the current installation was kept.");
        Directory.Move(staging, installed);
        var state = new InstalledState(manifest.Version, InstalledVersion);
        string temporary = StatePath + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(state)); File.Move(temporary, StatePath, overwrite: true);
        File.Delete(downloaded); status("Update installed");
    }
    public void Rollback()
    {
        var state = State(); if (state?.Previous is not string previous) throw new InvalidOperationException("No previous version is available.");
        bool baseline = System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(_root, "Daeva Sentinel.dll")).Version?.ToString(3) == previous;
        if (baseline) { File.Delete(StatePath); return; }
        if (!File.Exists(Path.Combine(VersionDirectory(previous), "Daeva Sentinel.exe"))) throw new IOException("Previous version is missing.");
        string temporary = StatePath + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(new InstalledState(previous, state.Version))); File.Move(temporary, StatePath, true);
    }
    public void Dispose() => _http.Dispose();
}
