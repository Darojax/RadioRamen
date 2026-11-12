using System;
using System.Threading;
using System.IO;

namespace RadioRamen;

internal sealed class AudioFormat
{
    internal readonly int Frequency, Channels;
    internal readonly PcmBuffer Buffer;
    internal readonly double StartSeconds;
    internal AudioFormat(int frequency, int channels, double startSeconds = 0) { Frequency = frequency; Channels = channels; StartSeconds = startSeconds; Buffer = new PcmBuffer(frequency * channels * 8); }
}

internal sealed class Playback
{
    internal readonly string Source;
    internal string Title;
    internal readonly bool Local;
    internal readonly double ResumeSeconds;
    internal readonly IntPtr Cookie;
    internal volatile AudioFormat? Format;
    internal volatile bool Cancelled, Finished;
    internal volatile string Status = "Connecting...", Failure = "";
    internal volatile string Transport = "", Diagnostic = "";
    internal long DecodedSamples;
    internal readonly CancellationTokenSource Cancellation = new();
    private static int _nextCookie;
    internal Playback(string source, string title, bool local, double resumeSeconds = 0) { Source = local ? Path.GetFullPath(source) : source; Title = title; Local = local; ResumeSeconds = local ? resumeSeconds : 0; Cookie = new IntPtr(Interlocked.Increment(ref _nextCookie)); }
}

// A single worker coalesces station changes. Network and decoding stay off Unity's main/audio threads.
internal sealed class Decoder : IDisposable
{
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _worker;
    private volatile Playback? _current;
    private volatile bool _closing;
    private readonly bool _managedHttp;
    internal Playback? Current => _current;

    internal Decoder(string nativeFolder, bool managedHttp = false)
    {
        _managedHttp = managedHttp;
        NativeAudio.Load(nativeFolder);
        _worker = new Thread(Work) { IsBackground = true, Name = "RadioRamen decoder" };
        _worker.Start();
    }

    internal Playback Play(string source, string title, bool local, double resumeSeconds = 0)
    {
        if (double.IsNaN(resumeSeconds) || double.IsInfinity(resumeSeconds) || resumeSeconds < 0) throw new ArgumentOutOfRangeException(nameof(resumeSeconds));
        Stop();
        var playback = new Playback(source, title, local, resumeSeconds);
        _current = playback; _wake.Set(); return playback;
    }

    internal void Stop()
    {
        var previous = _current; _current = null;
        if (previous != null)
        {
            previous.Cancelled = true;
            try { previous.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
            NativeAudio.Cancel(previous.Cookie);
        }
        _wake.Set();
    }

    private void Work()
    {
        Playback? last = null;
        try
        {
            NativeAudio.SetDevice(0);
            while (!_closing)
            {
                var playback = _current;
                if (playback == null || ReferenceEquals(playback, last)) { _wake.WaitOne(100); continue; }
                last = playback;
                Run(playback);
            }
        }
        finally { NativeAudio.SetDevice(0); NativeAudio.Free(); }
    }

    private void Run(Playback playback)
    {
        try
        {
            for (int attempt = 0; attempt < (playback.Local ? 1 : 3) && !playback.Cancelled && !_closing; attempt++)
            {
                uint channel = 0;
                HttpAudio? http = null;
                try
                {
                    if (attempt > 0)
                    {
                        playback.Status = "Reconnecting...";
                        for (int wait = 0; wait < attempt * 20 && !playback.Cancelled && !_closing; wait++) _wake.WaitOne(100);
                    }
                    if (playback.Cancelled || _closing) return;
                    uint flags = NativeAudio.Float | NativeAudio.Decode | NativeAudio.Unicode;
                    if (playback.Local) { channel = NativeAudio.OpenFile(false, playback.Source, 0, 0, flags | (playback.ResumeSeconds > 0 ? NativeAudio.Prescan : 0)); playback.Transport = "Local file"; }
                    else
                    {
                        if (!_managedHttp) { channel = NativeAudio.OpenUrl(playback.Source, 0, flags | NativeAudio.Block, IntPtr.Zero, playback.Cookie); playback.Transport = "Native HTTP"; }
                        if (_managedHttp || (channel == 0 && NativeAudio.Error() == 32))
                        {
                            http = new HttpAudio(playback.Source, playback.Cancellation.Token);
                            channel = http.Open(); playback.Transport = "Managed HTTP";
                        }
                    }
                    if (channel == 0) throw new InvalidOperationException("Could not open audio (decoder " + NativeAudio.Error() + ").");
                    if (!NativeAudio.Info(channel, out var info) || info.Channels < 1 || info.Channels > 2 || info.Frequency < 8000 || info.Frequency > 192000)
                        throw new InvalidOperationException("Unsupported audio sample format.");
                    double startSeconds = 0;
                    if (playback.Local && playback.ResumeSeconds > 0)
                    {
                        ulong length = NativeAudio.Length(channel, 0);
                        double duration = length == ulong.MaxValue ? 0 : NativeAudio.BytesToSeconds(channel, length);
                        // A completed or shortened file starts again rather than seeking past EOF.
                        if (duration > playback.ResumeSeconds)
                        {
                            if (!NativeAudio.SetPosition(channel, NativeAudio.SecondsToBytes(channel, playback.ResumeSeconds), 0))
                                throw new InvalidOperationException("Could not resume MP3 (decoder " + NativeAudio.Error() + ").");
                            ulong position = NativeAudio.GetPosition(channel, 0);
                            if (position == ulong.MaxValue) throw new InvalidOperationException("Could not read the resumed MP3 position.");
                            startSeconds = NativeAudio.BytesToSeconds(channel, position);
                        }
                    }
                    var format = new AudioFormat((int)info.Frequency, (int)info.Channels, startSeconds);
                    playback.Format = format; playback.Status = "Buffering...";
                    var samples = new float[8192];
                    int emptyReads = 0;
                    while (!playback.Cancelled && !_closing)
                    {
                        if (format.Buffer.Free < samples.Length) { _wake.WaitOne(20); continue; }
                        int bytes = NativeAudio.Read(channel, samples, (uint)(samples.Length * sizeof(float)));
                        if (bytes < 0)
                        {
                            int error = NativeAudio.Error();
                            if (playback.Local && error == 45) { playback.Status = "Finishing track..."; playback.Finished = true; return; }
                            throw new InvalidOperationException("Stream ended or became unavailable (decoder " + error + ").");
                        }
                        if (bytes == 0)
                        {
                            if (++emptyReads >= 300) throw new InvalidOperationException("No audio data received for six seconds.");
                            _wake.WaitOne(20); continue;
                        }
                        emptyReads = 0;
                        format.Buffer.Write(samples, bytes / sizeof(float));
                        Interlocked.Add(ref playback.DecodedSamples, bytes / sizeof(float));
                        playback.Status = "Playing";
                    }
                }
                catch (Exception ex)
                {
                    if (attempt == (playback.Local ? 0 : 2))
                    { playback.Failure = ex.GetBaseException().Message; playback.Diagnostic = ex.ToString(); playback.Status = "Unavailable - select again to retry"; }
                }
                finally { http?.Abort(); if (channel != 0) NativeAudio.Close(channel); http?.Dispose(); }
            }
        }
        catch (Exception ex) { playback.Failure = ex.Message; playback.Status = "Playback failed"; }
        finally { playback.Finished = true; playback.Cancellation.Dispose(); }
    }

    public void Dispose() { Stop(); _closing = true; _wake.Set(); }
    internal bool WaitForExit(int milliseconds) => _worker.Join(milliseconds);
}
