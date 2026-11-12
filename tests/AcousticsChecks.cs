using RadioRamen;

internal static class AcousticsChecks
{
    internal static void Run(string module, Action<bool, string> check)
    {
        bool Near(float a, float b) => Math.Abs(a - b) < .00001f;
        check(RadioRange.Nearby.All(p => Near(RadioRange.Gain(true, 50, p.Distance), p.Gain)), "accepted Nearby fade is retained at the 50 percent reference volume");
        foreach (bool nearby in new[] { false, true })
        {
            var reaches = new[] { 1, 10, 25, 50, 100 }.Select(v => RadioRange.Maximum(nearby, v)).ToArray();
            check(reaches.Zip(reaches.Skip(1)).All(p => p.First < p.Second), "lower radio volume shortens reach in " + (nearby ? "Nearby" : "Normal"));
            check(Enumerable.Range(0, 101).All(v => RadioRange.Gain(nearby, v, RadioRange.Maximum(nearby, v) + .001f) == 0), "every volume has a real silent outer boundary in " + nearby);
            check(Enumerable.Range(0, 80).All(d => Enumerable.Range(0, 100)
                .All(v => RadioRange.Gain(nearby, v, d) * v <= RadioRange.Gain(nearby, v + 1, d) * (v + 1) + .00001)), "volume cannot make a more distant/louder setting quieter in " + nearby);
            check(Enumerable.Range(1, 100).All(v => Enumerable.Range(0, 80)
                .All(d => RadioRange.Gain(nearby, v, d) >= RadioRange.Gain(nearby, v, d + 1))), "distance attenuation is monotonic in " + nearby);
            check(RadioRange.Gain(nearby, 0, 0) == 0 && RadioRange.Gain(nearby, 0, 5) == 0
                && RadioRange.Gain(nearby, 50, float.NaN) == 0 && RadioRange.Gain(nearby, 50, float.PositiveInfinity) == 0,
                "zero volume and unavailable distances remain silent in " + nearby);
            var keys = RadioRange.Keys(nearby);
            float Hermite(float t)
            {
                for (int i = 1; i < keys.Length; i++)
                {
                    if (t > keys[i].Time) continue;
                    var a = keys[i - 1]; var b = keys[i];
                    float span = b.Time - a.Time, u = (t - a.Time) / span;
                    return (2 * u * u * u - 3 * u * u + 1) * a.Gain + (u * u * u - 2 * u * u + u) * span * a.Outgoing
                        + (-2 * u * u * u + 3 * u * u) * b.Gain + (u * u * u - u * u) * span * b.Incoming;
                }
                return keys[^1].Gain;
            }
            check(keys[0].Time == 0 && keys[^1].Time == 1 && keys[0].Gain == 1 && keys[^1].Gain == 0,
                "exported native rolloff has full-volume and silent endpoints in " + nearby);
            float maximum = nearby ? RadioRange.NearbyMaximum : RadioRange.NormalMaximum;
            check(Enumerable.Range(0, 1001).All(i => Math.Abs(Hermite(i / 1000f) - RadioRange.Gain(nearby, 50, maximum * i / 1000f)) < .00002f),
                "native Hermite curve keys reproduce the accepted linear fade without overshoot in " + nearby);
        }
        check(Near(RadioRange.Gain(true, 10, 6), 0) && RadioRange.Gain(true, 50, 6) > 0 && RadioRange.Gain(true, 100, 6) > RadioRange.Gain(true, 50, 6),
            "at the same outside distance a low-volume radio is silent while louder radios carry farther");
        check(RadioRange.Gain(false, 50, 29) > 0 && RadioRange.Gain(false, 50, 30) == 0, "Normal no longer has a logarithmic tail beyond its intended limit");
        check(Enumerable.Range(0, 200).All(i => RadioRange.Gain(true, 100, i / 10f) - RadioRange.Gain(true, 100, i / 10f + .2f) < .05f),
            "small distance steps at full volume stay gradual in the distance-only curve");

        var first = TuningSignal.Create(); var repeat = TuningSignal.Create();
        check(first.SequenceEqual(repeat), "the original fixed tuning texture is restored reproducibly");
        check(first.Length == 22050 * 4 && first.Any(v => v != 0), "the original four-second tuning loop is audible");
        var rms = Enumerable.Range(0, TuningSignal.Seconds * 4).Select(k => Math.Sqrt(first.Skip(k * TuningSignal.Frequency / 4).Take(TuningSignal.Frequency / 4).Average(x => (double)x * x))).ToArray();
        check(rms.Max() < rms.Min() * 1.25, "the preferred steady tuning texture replaces the new squelch dips");
        check(first.Zip(first.Skip(1)).Max(p => Math.Abs(p.First - p.Second)) < .5, "analogue tuning has no unbounded sample discontinuities");

        // A listenable offline artifact; never played automatically or installed.
        string path = Path.Combine(module, "artifacts/tuning-0.2.2.wav");
        using var output = new BinaryWriter(File.Create(path));
        output.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); output.Write(36 + first.Length * 2);
        output.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); output.Write(16); output.Write((short)1); output.Write((short)1);
        output.Write(TuningSignal.Frequency); output.Write(TuningSignal.Frequency * 2); output.Write((short)2); output.Write((short)16);
        output.Write(System.Text.Encoding.ASCII.GetBytes("data")); output.Write(first.Length * 2);
        foreach (float sample in first) output.Write((short)Math.Round(sample * TuningSignal.Volume * short.MaxValue));
    }
}
