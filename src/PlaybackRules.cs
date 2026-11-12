using System;

namespace RadioRamen;

internal static class PlaybackRules
{
    internal static bool PauseLocal(bool local, bool powered, bool gamePaused) => local && (!powered || gamePaused);
    internal static bool Mute(bool local, bool powered, bool gamePaused, bool muted) => !powered || muted || (!local && gamePaused);
    internal static bool Tune(bool local, bool powered, bool gamePaused, bool muted, bool started, bool failed, bool finished)
        => !local && powered && !gamePaused && !muted && !started && !failed && !finished;
}

internal sealed class PlaybackClock
{
    private double _elapsed, _started;
    internal bool Running { get; private set; }
    internal double Elapsed(double now) => _elapsed + (Running ? Math.Max(0, now - _started) : 0);
    internal void Resume(double now) { if (!Running) { _started = now; Running = true; } }
    internal void Pause(double now) { _elapsed = Elapsed(now); Running = false; }
    internal void Reset(double elapsed = 0) { _elapsed = elapsed; Running = false; }
}
