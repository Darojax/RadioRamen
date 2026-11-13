using System;
using System.Diagnostics;
using System.Threading;

namespace RadioRamen;

// One bounded worker, one replaceable pending snapshot. The publisher never
// performs disk I/O and the worker never reads Unity objects or mutable settings.
internal sealed class LatestWork<T> : IDisposable where T : class
{
    private readonly object _gate = new();
    private readonly Action<T> _write;
    private T? _pending;
    private bool _running, _closed;
    private string? _error;
    internal LatestWork(Action<T> write) { _write=write; }
    internal void Submit(T snapshot)
    {
        lock(_gate)
        {
            if(_closed)return;
            _pending=snapshot;
            if(_running)return;
            _running=true;
            ThreadPool.QueueUserWorkItem(static state=>((LatestWork<T>)state!).Run(),this);
        }
    }
    private void Run()
    {
        while(true)
        {
            T value;
            lock(_gate)
            {
                if(_pending==null){_running=false;Monitor.PulseAll(_gate);return;}
                value=_pending;_pending=null;
            }
            try{_write(value);}catch(Exception ex){Interlocked.Exchange(ref _error,ex.Message);}
        }
    }
    internal string? TakeError()=>Interlocked.Exchange(ref _error,null);
    // Only shutdown calls this in production; normal gameplay never waits.
    internal bool Flush(int milliseconds)
    {
        var timer=Stopwatch.StartNew();
        lock(_gate)while(_running||_pending!=null)
        {
            int remaining=milliseconds-(int)timer.ElapsedMilliseconds;
            if(remaining<=0||!Monitor.Wait(_gate,remaining))return false;
        }
        return true;
    }
    public void Dispose(){lock(_gate)_closed=true;}
}
