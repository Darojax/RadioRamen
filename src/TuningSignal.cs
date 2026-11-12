using System;

namespace RadioRamen;

// A quiet analogue dial sweep: changing bands of hiss, short carrier beat
// whistles, crackle and squelch dips. No recordings or broadcast fragments.
internal static class TuningSignal
{
    internal const int Frequency = 22050, Seconds = 8;
    internal const float Volume = 0.16f, FadeSeconds = .12f;

    internal static float[] Create(uint seed = 0x52414D45)
    {
        var samples = new float[Frequency * Seconds];
        uint random = seed == 0 ? 0x52414D45 : seed;
        double low = 0, slow = 0, carrierPhase = 0, flutterPhase = 0, crackle = 0;
        double[] carriers = { .72, 1.85, 3.18, 4.65, 5.72, 7.14 };
        for (int i = 0; i < samples.Length; i++)
        {
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            double white = random / (double)uint.MaxValue * 2 - 1;
            double seconds = i / (double)Frequency;
            double movement = .5 + .5 * Math.Sin(seconds * 1.31 + .32 * Math.Sin(seconds * 2.17));
            low += (.24 + .48 * movement) * (white - low); slow += .045 * (white - slow);
            double strength = 0;
            foreach (double carrier in carriers)
            {
                double x = Math.Max(0, 1 - Math.Abs(seconds - carrier) / .38);
                strength = Math.Max(strength, x * x * (3 - 2 * x));
            }
            double detune = 1 - strength;
            carrierPhase += 2 * Math.PI * (180 + 2350 * detune * detune) / Frequency;
            flutterPhase += 2 * Math.PI * (44 + 25 * movement) / Frequency;
            crackle *= .965;
            if (Math.Abs(white) > .9992) crackle = white * .13;
            double hiss = (low - slow) * (.35 + .07 * Math.Sin(seconds * 2.9)) * (1 - .82 * strength);
            double shoulder = 4 * strength * (1 - strength);
            double whistle = Math.Sin(carrierPhase) * .022 * shoulder * (.7 + .3 * Math.Sin(flutterPhase));
            double edge = Math.Min(1, Math.Min(i, samples.Length - 1 - i) / (Frequency * .018));
            samples[i] = (float)((hiss + whistle + crackle * .22) * edge);
        }
        return samples;
    }
}

internal static class RadioPromptText
{
    internal static string Operate(string text)
    {
        foreach (string old in new[] { "change the channel", "change channel" })
        {
            int at = text.IndexOf(old, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) return text.Substring(0, at) + "operate" + text.Substring(at + old.Length);
        }
        return text;
    }
}
