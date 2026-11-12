using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;

namespace RadioRamen;

// Stream the response body into BASS's bounded file callbacks. Nothing is downloaded in full.
internal sealed class HttpAudio : IDisposable
{
    private readonly HttpClient _client;
    private readonly HttpResponseMessage _response;
    private readonly System.IO.Stream _stream;
    private readonly CancellationTokenSource _cancel;
    private readonly byte[] _scratch = new byte[32768];
    private NativeAudio.FileProcs _callbacks;
    internal string Failure = "";

    internal HttpAudio(string url, CancellationToken stop)
    {
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(stop);
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("RadioRamen/0.1");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Icy-MetaData", "0");
            _response = _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, _cancel.Token).GetAwaiter().GetResult();
            _response.EnsureSuccessStatusCode();
            _stream = _response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            _callbacks = new NativeAudio.FileProcs { Close = _ => { }, Length = _ => 0, Read = Read, Seek = (_, __) => false };
        }
        catch { _response?.Dispose(); _client.Dispose(); _cancel.Dispose(); throw; }
    }

    internal uint Open()
        => NativeAudio.OpenUser(1, NativeAudio.Float | NativeAudio.Decode | NativeAudio.Block, ref _callbacks, IntPtr.Zero);

    private uint Read(IntPtr destination, uint length, IntPtr user)
    {
        try
        {
            int count = _stream.ReadAsync(_scratch, 0, (int)Math.Min(length, (uint)_scratch.Length), _cancel.Token).GetAwaiter().GetResult();
            if (count > 0) Marshal.Copy(_scratch, 0, destination, count);
            return (uint)count;
        }
        catch (Exception ex) { if (!_cancel.IsCancellationRequested) Failure = ex.GetBaseException().Message; return 0; }
    }

    internal void Abort() { _cancel.Cancel(); _client.CancelPendingRequests(); }
    public void Dispose()
    {
        Abort(); _stream.Dispose(); _response.Dispose(); _client.Dispose(); _cancel.Dispose();
        GC.KeepAlive(_callbacks);
    }
}
