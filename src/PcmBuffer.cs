using System;

namespace RadioRamen;

// The audio callback only copies ready samples; it never opens files or waits for a server.
internal sealed class PcmBuffer
{
    private readonly float[] _samples;
    private readonly object _gate = new();
    private int _read, _write, _count;
    internal PcmBuffer(int capacity) { if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); _samples = new float[capacity]; }
    internal int Capacity => _samples.Length;
    internal int Available { get { lock (_gate) return _count; } }
    internal int Free { get { lock (_gate) return Capacity - _count; } }

    internal int Write(float[] source, int count)
    {
        lock (_gate)
        {
            int length = Math.Min(count, Capacity - _count), first = Math.Min(length, Capacity - _write);
            Array.Copy(source, 0, _samples, _write, first);
            Array.Copy(source, first, _samples, 0, length - first);
            _write = (_write + length) % Capacity; _count += length;
            return length;
        }
    }

    internal int Read(float[] destination)
    {
        lock (_gate)
        {
            int length = Math.Min(destination.Length, _count), first = Math.Min(length, Capacity - _read);
            Array.Copy(_samples, _read, destination, 0, first);
            Array.Copy(_samples, 0, destination, first, length - first);
            Array.Clear(destination, length, destination.Length - length);
            _read = (_read + length) % Capacity; _count -= length;
            return length;
        }
    }
}
