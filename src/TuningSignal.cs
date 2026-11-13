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
    internal const string InteractKey = "<sprite=\"spritesheet_desk\" name=\"e\">";
    private static readonly Regex Sprite = new(@"<sprite\b[^>]*\bname\s*=\s*(?:""(?<key>[^""]+)""|'(?<key>[^']+)'|(?<key>[^\s>]+))[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PlainKey = new(@"(?<=Press\s)(?:E|LMB)(?=\s)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string Controls(string text)
    {
        foreach (string old in new[] { "change the channel", "change channel" })
        {
            int at = text.IndexOf(old, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            string result = text.Substring(0, at) + "interact" + text.Substring(at + old.Length);
            // One pass prevents a newly substituted E icon being swapped back.
            result = Sprite.Replace(result, match => Swap(match.Groups["key"].Value) ?? match.Value);
            return PlainKey.Replace(result, match => Swap(match.Value) ?? match.Value);
        }
        return text;
    }

    private static string? Swap(string key) => key.ToLowerInvariant() switch
    {
        "e" => LeftMouse,
        "ml" or "leftmouse" or "lmb" => InteractKey,
        _ => null
    };
}
