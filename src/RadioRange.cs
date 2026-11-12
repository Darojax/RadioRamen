using System;

namespace RadioRamen;

internal static class RadioRange
{
    internal const float NormalMinimum = 2.5f, NormalMaximum = 30f;
    internal const float NearbyMinimum = 2f, NearbyMaximum = 12f;

    // The accepted Nearby fade is the reference. Reach follows the
    // square root of radio gain; master/game volume is a listening preference.
    internal static float Scale(int volume) => (float)Math.Sqrt(Math.Max(0, Math.Min(100, volume)) / 50f);
    internal static float Maximum(bool nearby, int volume) => (nearby ? NearbyMaximum : NormalMaximum) * Scale(volume);

    // Match the old wide logarithmic curve up close, then reach actual silence.
    // Unity's built-in logarithmic rolloff does not stop at maxDistance.
    internal static readonly (float Distance, float Gain)[] Normal = {
        (0f, 1f), (2.5f, 1f), (5f, 0.5f), (10f, 0.25f),
        (20f, 0.125f), (24f, 0.10f), (27f, 0.05f), (30f, 0f)
    };

    // Distance in game units, then a multiplier of the selected radio volume.
    // Linear segments prevent overshoot between these quiet falloff points.
    internal static readonly (float Distance, float Gain)[] Nearby = {
        (0f, 1f), (2f, 1f), (4f, 0.35f), (6f, 0.10f),
        (8f, 0.02f), (10f, 0.003f), (12f, 0f)
    };

    // Unity's rolloff curve uses distance / maxDistance. These separate linear
    // tangents reproduce the accepted Nearby curve, with no corner overshoot.
    // Volume changes the physical radius, not these normalized curve keys.
    internal static RolloffKey[] Keys(bool nearby)
    {
        var points = nearby ? Nearby : Normal;
        float maximum = nearby ? NearbyMaximum : NormalMaximum;
        var keys = new RolloffKey[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            float incoming = i == 0 ? 0 : (points[i].Gain - points[i - 1].Gain) / (points[i].Distance - points[i - 1].Distance) * maximum;
            float outgoing = i + 1 == points.Length ? 0 : (points[i + 1].Gain - points[i].Gain) / (points[i + 1].Distance - points[i].Distance) * maximum;
            keys[i] = new RolloffKey(points[i].Distance / maximum, points[i].Gain, incoming, outgoing);
        }
        return keys;
    }

    internal static float Gain(bool nearby, int volume, float distance)
    {
        float scale = Scale(volume);
        if (scale <= 0 || float.IsNaN(distance) || float.IsInfinity(distance)) return 0;
        distance = Math.Max(0, distance) / scale;
        var points = nearby ? Nearby : Normal;
        for (int i = 1; i < points.Length; i++)
        {
            if (distance > points[i].Distance) continue;
            float t = (distance - points[i - 1].Distance) / (points[i].Distance - points[i - 1].Distance);
            return points[i - 1].Gain + Math.Max(0, t) * (points[i].Gain - points[i - 1].Gain);
        }
        return 0;
    }
}

internal readonly struct RolloffKey
{
    internal readonly float Time, Gain, Incoming, Outgoing;
    internal RolloffKey(float time, float gain, float incoming, float outgoing)
    { Time = time; Gain = gain; Incoming = incoming; Outgoing = outgoing; }
}
