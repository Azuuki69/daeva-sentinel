using System.IO;
using System.Security.Cryptography;
using System.Text;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using BakarmaCoach.Core;

namespace BakarmaCoach;

/// <summary>Local Windows speech, cached as WAV before a fight; no cloud request or paid service.</summary>
public sealed class Speech : IDisposable
{
    public sealed record VoiceChoice(string Id, string DisplayName, string Language);
    public const string BundledVoice = "piper-en-US-ljspeech";
    public static IReadOnlyList<VoiceChoice> Voices => new[] { new VoiceChoice(BundledVoice, "English · bundled neural voice", "en-US") }
        .Concat(SpeechSynthesizer.AllVoices.Where(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            .Select(v => new VoiceChoice(v.Id, v.DisplayName, v.Language))).ToArray();
    public string VoiceId { get; set; } = BundledVoice;
    public double Volume { get; set; } = 0.8;
    public bool Enabled { get; set; } = true;
    public event Action<string>? Error;
    private readonly string _cache;
    private readonly SemaphoreSlim _synthGate = new(1);
    private MediaPlayer? _player;
    private MediaSource? _source;
    private long _generation;
    private DateTime _until;
    private int _priority;
    public int OpenedClips { get; private set; }
    public string? LastError { get; private set; }

    public Speech(string data) { _cache = Path.Combine(data, "voice-cache"); Directory.CreateDirectory(_cache); }

    public async Task<bool> PrepareAsync()
    {
        var lines = Encounter.Rules.Values.Select(x => x.Call).Concat(new[]
        {
            "Water bombs incoming. Watch the ground.", "Wave phase soon. Save your stagger skills.",
            "Stagger now. Use your stagger skills.", "Stagger broken. Attack Bakarma.",
            "Voice alerts are ready. Watch the ground and save your stagger skills.", "1", "2", "3"
        }).Distinct();
        try { foreach (string line in lines) await PrepareLine(line, VoiceId); return true; }
        catch (Exception ex) { LastError = ex.Message; Error?.Invoke("Voice preparation failed: " + ex.Message); return false; }
    }

    private string PathFor(string text, string voice) => Path.Combine(_cache,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(voice + "|" + text)))[..24] + ".wav");

    private async Task<string> PrepareLine(string text, string voice)
    {
        if (voice == BundledVoice)
        {
            string bundled = Path.Combine(AppContext.BaseDirectory, "voicepack", Path.GetFileName(PathFor(text, voice)));
            if (!File.Exists(bundled)) throw new FileNotFoundException("Bundled English clip is missing. Extract the complete ZIP.", bundled);
            return bundled;
        }
        string path = PathFor(text, voice);
        await _synthGate.WaitAsync();
        try
        {
            if (File.Exists(path)) return path;
            using var synth = new SpeechSynthesizer();
            if (SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voice && v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) is { } choice) synth.Voice = choice;
            else throw new InvalidOperationException("The selected English Windows voice is unavailable. Choose the bundled English voice.");
            using var audio = await synth.SynthesizeTextToStreamAsync(text);
            using var stream = audio.AsStreamForRead();
            using var file = File.Create(path + ".tmp");
            await stream.CopyToAsync(file);
            file.Close(); File.Move(path + ".tmp", path, true);
            return path;
        }
        finally { _synthGate.Release(); }
    }

    public async void Say(Warning warning)
    {
        DateTime now = DateTime.Now;
        if (!Enabled || now >= warning.Expires || now < _until && warning.Priority < _priority) return;
        _priority = warning.Priority;
        _until = warning.Expires;
        long mine = Interlocked.Increment(ref _generation);
        try
        {
            string path = await PrepareLine(warning.Text, VoiceId);
            if (mine != Volatile.Read(ref _generation) || !Enabled || DateTime.Now >= warning.Expires) return;
            StopPlayer();
            _source = MediaSource.CreateFromUri(new Uri(path));
            _player = new MediaPlayer { Source = _source, Volume = Math.Clamp(Volume, 0, 1) };
            _player.MediaEnded += (_, _) => { if (mine == Volatile.Read(ref _generation)) { _until = DateTime.MinValue; _priority = 0; } };
            _player.MediaOpened += (_, _) => OpenedClips++;
            _player.MediaFailed += (_, args) => { LastError = args.ErrorMessage; Error?.Invoke("Audio playback failed: " + args.ErrorMessage); };
            _player.Play();
        }
        catch (Exception ex) { LastError = ex.Message; Error?.Invoke("Voice unavailable: " + ex.Message); }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation); _until = DateTime.MinValue; _priority = 0; StopPlayer();
    }
    private void StopPlayer() { _player?.Pause(); _player?.Dispose(); _player = null; _source?.Dispose(); _source = null; }
    public void Dispose() => Stop();
}
