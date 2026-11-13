using System;
using System.Collections.Generic;

namespace RadioRamen;

// A small room/entrance approximation, not a wall-material or wave solver.
// Coordinates are horizontal metres in the room's scaled local frame.
internal readonly struct RoomPoint
{
    internal readonly float X, Z;
    internal RoomPoint(float x, float z) { X = x; Z = z; }
    internal static float Distance(RoomPoint a, RoomPoint b)
    { float x = a.X - b.X, z = a.Z - b.Z; return (float)Math.Sqrt(x * x + z * z); }
}

internal readonly struct RoomFootprint
{
    internal readonly float X, Z;
    internal RoomFootprint(float halfX, float halfZ) { X = halfX; Z = halfZ; }
    // The weather/placement trigger includes the wall slab. Its acoustic
    // interior is inset, so a listener just beyond the brick is not indoors.
    internal static RoomFootprint Restaurant(float halfX,float halfZ) => new(Math.Max(.5f,halfX-.12f),Math.Max(.5f,halfZ-.12f));
    internal bool Contains(RoomPoint p) => Math.Abs(p.X) <= X && Math.Abs(p.Z) <= Z;
    internal bool ContainsSource(RoomPoint p) => OutsideDistance(p) <= .35f;
    internal RoomPoint InsetSource(RoomPoint p) => new(Math.Max(-X+.01f,Math.Min(X-.01f,p.X)),Math.Max(-Z+.01f,Math.Min(Z-.01f,p.Z)));
    internal float OutsideDistance(RoomPoint p)
    {
        float x = Math.Max(0, Math.Abs(p.X) - X), z = Math.Max(0, Math.Abs(p.Z) - Z);
        return (float)Math.Sqrt(x * x + z * z);
    }
    internal float BoundaryDistance(RoomPoint p) => Contains(p)
        ? Math.Min(X - Math.Abs(p.X), Z - Math.Abs(p.Z)) : OutsideDistance(p);
    internal RoomPoint OnBoundary(RoomPoint p)
    {
        float x = Math.Max(-X, Math.Min(X, p.X)), z = Math.Max(-Z, Math.Min(Z, p.Z));
        return X - Math.Abs(x) < Z - Math.Abs(z)
            ? new RoomPoint(x < 0 ? -X : X, z) : new RoomPoint(x, z < 0 ? -Z : Z);
    }
    // Clip against a slightly inset rectangle: travelling along an exterior
    // edge is allowed, but a shortcut through the building is not.
    internal bool CrossesInterior(RoomPoint a, RoomPoint b)
    {
        float low = 0, high = 1;
        return Slab(a.X, b.X - a.X, X - .001f, ref low, ref high)
            && Slab(a.Z, b.Z - a.Z, Z - .001f, ref low, ref high) && high >= low;
    }
    private static bool Slab(float at, float delta, float half, ref float low, ref float high)
    {
        if (Math.Abs(delta) < .000001f) return Math.Abs(at) < half;
        float a = (-half - at) / delta, b = (half - at) / delta;
        if (a > b) { float swap = a; a = b; b = swap; }
        low = Math.Max(low, a); high = Math.Min(high, b); return high >= low;
    }
}

internal sealed class RoomRoute
{
    private readonly RoomPoint[] _nodes = new RoomPoint[6];
    private readonly float[] _distance = new float[6];
    private readonly bool[] _visited = new bool[6];

    internal float ThroughEntrance(RoomFootprint room, RoomPoint inside, RoomPoint outside, IReadOnlyList<RoomPoint> openings)
    {
        if (!room.Contains(inside) || room.Contains(outside) || openings.Count == 0) return float.NaN;
        float best = float.PositiveInfinity;
        RoomPoint previous = default;
        bool hasPrevious = false;
        foreach (var opening in openings)
        {
            var door = room.OnBoundary(opening);
            // Adjacent samples describe one continuous opening. Restricting
            // sound to isolated points adds a small artificial detour exactly
            // at the threshold between samples, causing an audible gain step.
            if (hasPrevious && CrossesOpening(inside, outside, previous, door))
                return RoomPoint.Distance(inside, outside);
            best = Math.Min(best, RoomPoint.Distance(inside, door) + Outside(room, door, outside));
            previous = door; hasPrevious = true;
        }
        return best;
    }

    private static bool CrossesOpening(RoomPoint inside, RoomPoint outside, RoomPoint a, RoomPoint b)
    {
        float dx=outside.X-inside.X,dz=outside.Z-inside.Z,t;
        if (Math.Abs(a.X-b.X)<.000001f && Math.Abs(dx)>.000001f)
        {
            t=(a.X-inside.X)/dx;
            float z=inside.Z+dz*t;
            return t>=0 && t<=1 && z>=Math.Min(a.Z,b.Z) && z<=Math.Max(a.Z,b.Z);
        }
        if (Math.Abs(a.Z-b.Z)<.000001f && Math.Abs(dz)>.000001f)
        {
            t=(a.Z-inside.Z)/dz;
            float x=inside.X+dx*t;
            return t>=0 && t<=1 && x>=Math.Min(a.X,b.X) && x<=Math.Max(a.X,b.X);
        }
        return false;
    }

    internal float Outside(RoomFootprint room, RoomPoint door, RoomPoint outside)
    {
        _nodes[0] = door; _nodes[1] = new RoomPoint(-room.X, -room.Z); _nodes[2] = new RoomPoint(room.X, -room.Z);
        _nodes[3] = new RoomPoint(room.X, room.Z); _nodes[4] = new RoomPoint(-room.X, room.Z); _nodes[5] = outside;
        for (int i = 0; i < 6; i++) { _distance[i] = float.PositiveInfinity; _visited[i] = false; }
        _distance[0] = 0;
        for (int step = 0; step < 6; step++)
        {
            int at = -1;
            for (int i = 0; i < 6; i++) if (!_visited[i] && (at < 0 || _distance[i] < _distance[at])) at = i;
            if (at < 0 || float.IsPositiveInfinity(_distance[at])) break;
            if (at == 5) return _distance[at];
            _visited[at] = true;
            for (int i = 0; i < 6; i++)
                if (!_visited[i] && !room.CrossesInterior(_nodes[at], _nodes[i]))
                    _distance[i] = Math.Min(_distance[i], _distance[at] + RoomPoint.Distance(_nodes[at], _nodes[i]));
        }
        return float.NaN;
    }
}

internal sealed class RoomSoundFade
{
    internal const float ClosedCutoff = 1600f;
    internal float Amount { get; private set; }
    internal float Gain { get; private set; } = 1;
    internal float Cutoff => (float)(22000 * Math.Pow(ClosedCutoff / 22000d, Amount));
    internal void Reset() { Amount=0;Gain=1; }
    internal static float Target(float direct, float route)
    {
        if (!Finite(direct) || !Finite(route) || direct < 0 || route < direct) return 0;
        return 1-RadioRange.EntranceVisibility(route-direct);
    }
    internal static float RouteGain(int volume,float direct,float route)
    {
        if(!Finite(direct)||!Finite(route)||direct<0||route<direct)return 1;
        float native=RadioRange.Gain(true,volume,direct),travel=RadioRange.GainAlongRoute(volume,route,route-direct);
        if(native<=.000001f)return 1;
        // The native entrance is slatted wood, not a solid acoustic door.
        // Move the old closed-door loss onto solid-wall detours, increasing
        // gradually as the path bends away from the opening. Long rear-wall
        // paths retain their accepted quiet level; the threshold stays clear.
        float transmission=1-.82f*Target(direct,route);
        float bend=(float)Math.Exp(-Math.Max(0,route-direct-.15f)/2);
        return Math.Max(0,Math.Min(1,travel/native*transmission*bend));
    }
    internal void Advance(float target, float gain, float seconds)
    {
        if (!Finite(target) || !Finite(gain) || !Finite(seconds) || seconds < 0) return;
        target = Math.Max(0, Math.Min(1, target));
        float tau = target > Amount ? .65f : .35f;
        Amount += (target - Amount) * (1 - (float)Math.Exp(-Math.Min(seconds, .05f) / tau));
        gain=Math.Max(0,Math.Min(1,gain));
        float gainTau=gain<Gain?.65f:.35f;
        Gain+=(gain-Gain)*(1-(float)Math.Exp(-Math.Min(seconds,.05f)/gainTau));
        if(gain==0&&Gain<.0001f)Gain=0;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
