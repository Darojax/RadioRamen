namespace RadioRamen;

internal static class RadioRange
{
    internal const float NormalMinimum = 2.5f, NormalMaximum = 30f;
    internal const float NearbyMinimum = 2f, NearbyMaximum = 12f;

    // Distance in game units, then a multiplier of the selected radio volume.
    // Linear segments prevent overshoot between these quiet falloff points.
    internal static readonly (float Distance, float Gain)[] Nearby = {
        (0f, 1f), (2f, 1f), (4f, 0.35f), (6f, 0.10f),
        (8f, 0.02f), (10f, 0.003f), (12f, 0f)
    };
}
