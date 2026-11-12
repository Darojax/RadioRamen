using System;
using System.Text.RegularExpressions;

namespace RadioRamen;

// The original quiet, band-limited tuning texture, restored by player request.
internal static class TuningSignal
{
    internal const int Frequency = 22050, Seconds = 4;
    internal const float Volume = 0.16f;

    internal static float[] Create()
    {
        var samples = new float[Frequency * 4];
        uint random = 0x52414D45;
        double low = 0, slow = 0, phase = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            double white = random / (double)uint.MaxValue * 2 - 1;
            low += 0.48 * (white - low); slow += 0.08 * (white - slow);
            double seconds = i / (double)Frequency;
            double movement = Math.Sin(seconds * Math.PI * 0.5);
            phase += 2 * Math.PI * (900 + 650 * movement) / Frequency;
            double edge = Math.Min(1, Math.Min(i, samples.Length - 1 - i) / (Frequency * 0.012));
            samples[i] = (float)(((low - slow) * 0.42 + Math.Sin(phase) * 0.008) * edge);
        }
        return samples;
    }
}

internal static class RadioPromptText
{
    internal const string LeftMouse = "<sprite=\"spritesheet_desk\" name=\"ml\">";
    private static readonly Regex InteractKey = new(@"<sprite\b[^>]*\bname\s*=\s*(?:""e""|'e'|e)(?=\s|>)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string Interact(string text)
    {
        string result = InteractKey.Replace(text, LeftMouse);
        return result == text ? Regex.Replace(text, @"^\s*E(?=\s|$)", LeftMouse, RegexOptions.CultureInvariant) : result;
    }

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
