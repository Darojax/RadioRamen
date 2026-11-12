using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RadioRamen;

internal static class Ini
{
    internal static Dictionary<string, Dictionary<string, string>> Read(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? section = null;
        int number = 0;
        foreach (string raw in text.Split('\n'))
        {
            number++;
            string line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                string name = line.Substring(1, line.Length - 2).Trim();
                if (name.Length == 0 || result.ContainsKey(name)) throw new InvalidDataException("Duplicate or empty INI section at line " + number);
                result.Add(name, section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                continue;
            }
            int equals = line.IndexOf('=');
            if (section == null || equals < 1) throw new InvalidDataException("Expected Section and Key = Value at line " + number);
            string key = line.Substring(0, equals).Trim();
            if (section.ContainsKey(key)) throw new InvalidDataException("Duplicate INI key at line " + number);
            // Stream URLs can legitimately contain both semicolons and fragments.
            section.Add(key, line.Substring(equals + 1).Trim());
        }
        return result;
    }

    internal static string Value(Dictionary<string, string> section, string key, string fallback = "")
        => section.TryGetValue(key, out string? value) ? value : fallback;

    internal static bool Boolean(Dictionary<string, string> section, string key, bool fallback)
        => bool.TryParse(Value(section, key, fallback.ToString()), out bool value) ? value : throw new InvalidDataException(key + " must be true or false.");
}

internal enum SoundProfile { Clean, FM, AM }
internal enum SavedSource { Radio, Music }

internal sealed class RadioSettings
{
    internal bool Enabled = true, Muted, Shuffle, Repeat = true, ShortRange;
    internal int Volume = 50;
    internal string LastStation = "fm-setagaya";
    internal SoundProfile Sound = SoundProfile.Clean;
    internal SavedSource LastSource;
    internal string LastTrack = "";
    internal double LastTrackPosition;

    internal static RadioSettings Parse(string text)
    {
        var all = Ini.Read(text);
        var settings = new RadioSettings();
        if (all.TryGetValue("Radio", out var radio))
        {
            settings.Enabled = Ini.Boolean(radio, "Enabled", true);
            settings.Muted = Ini.Boolean(radio, "Muted", false);
            settings.ShortRange = Ini.Boolean(radio, "ShortRange", false);
            settings.LastStation = Ini.Value(radio, "LastStation", settings.LastStation);
            if (!Enum.TryParse(Ini.Value(radio, "Sound", "Clean"), true, out settings.Sound) || !Enum.IsDefined(typeof(SoundProfile), settings.Sound))
                throw new InvalidDataException("Sound must be Clean, FM or AM.");
            if (!int.TryParse(Ini.Value(radio, "Volume", "50"), NumberStyles.Integer, CultureInfo.InvariantCulture, out settings.Volume)
                || settings.Volume < 0 || settings.Volume > 100) throw new InvalidDataException("Volume must be from 0 to 100.");
        }
        if (all.TryGetValue("Music", out var music))
        {
            settings.Shuffle = Ini.Boolean(music, "Shuffle", false);
            settings.Repeat = Ini.Boolean(music, "Repeat", true);
        }
        if (all.TryGetValue("Playback", out var playback))
        {
            if (!Enum.TryParse(Ini.Value(playback, "Source", "Radio"), true, out settings.LastSource)
                || !Enum.IsDefined(typeof(SavedSource), settings.LastSource)) throw new InvalidDataException("Playback Source must be Radio or Music.");
            settings.LastTrack = Ini.Value(playback, "Track");
            if (settings.LastTrack.Length > 255 || settings.LastTrack.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || settings.LastTrack == "." || settings.LastTrack == "..") throw new InvalidDataException("Playback Track must be an MP3 filename inside the Music folder.");
            if (!double.TryParse(Ini.Value(playback, "Position", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out settings.LastTrackPosition)
                || double.IsNaN(settings.LastTrackPosition) || double.IsInfinity(settings.LastTrackPosition) || settings.LastTrackPosition < 0)
                throw new InvalidDataException("Playback Position must be a non-negative number of seconds.");
        }
        return settings;
    }

    internal void Save(string path)
    {
        // Keep comments and custom entries when remembering controls.
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["Radio.Volume"] = Volume.ToString(CultureInfo.InvariantCulture), ["Radio.Muted"] = Muted.ToString().ToLowerInvariant(),
            ["Radio.LastStation"] = LastStation, ["Radio.Sound"] = Sound.ToString(), ["Radio.ShortRange"] = ShortRange.ToString().ToLowerInvariant(),
            ["Music.Shuffle"] = Shuffle.ToString().ToLowerInvariant(), ["Music.Repeat"] = Repeat.ToString().ToLowerInvariant(),
            ["Playback.Source"] = LastSource.ToString(), ["Playback.Track"] = LastTrack,
            ["Playback.Position"] = LastTrackPosition.ToString("0.######", CultureInfo.InvariantCulture)
        };
        var lines = File.ReadAllLines(path).ToList();
        foreach (var entry in values)
        {
            string[] parts = entry.Key.Split('.');
            int start = lines.FindIndex(line => line.Trim().Equals("[" + parts[0] + "]", StringComparison.OrdinalIgnoreCase));
            if (start < 0) { lines.Add(""); lines.Add("[" + parts[0] + "]"); start = lines.Count - 1; }
            int end = start + 1;
            while (end < lines.Count && !lines[end].TrimStart().StartsWith("[")) end++;
            int existing = -1;
            for (int i = start + 1; i < end; i++)
            {
                int equals = lines[i].IndexOf('=');
                if (equals > 0 && lines[i].Substring(0, equals).Trim().Equals(parts[1], StringComparison.OrdinalIgnoreCase)) { existing = i; break; }
            }
            string replacement = parts[1] + " = " + entry.Value;
            if (existing >= 0) lines[existing] = replacement; else lines.Insert(end, replacement);
        }
        string temporary = path + ".tmp";
        File.WriteAllLines(temporary, lines, new System.Text.UTF8Encoding(false));
        File.Replace(temporary, path, null);
    }
}

internal sealed class Station
{
    internal string Id = "", Name = "", Region = "", Category = "Local & Talk", Frequency = "", Stream = "", Website = "";
    internal bool IsCustom;
    internal string Detail => string.Join("  /  ", new[] { Region, Frequency, Category, IsCustom ? "Custom" : "" }.Where(x => x.Length > 0));

    internal static List<Station> Parse(string text)
        => ParseSections(Ini.Read(text));

    internal static List<Station> ParseSections(IEnumerable<KeyValuePair<string, Dictionary<string, string>>> sections)
    {
        var result = new List<Station>();
        foreach (var section in sections)
        {
            var s = new Station { Id = section.Key, Name = Ini.Value(section.Value, "Name"), Region = Ini.Value(section.Value, "Region"),
                Category = Ini.Value(section.Value, "Category", "Local & Talk"), Frequency = Ini.Value(section.Value, "Frequency"),
                Stream = Ini.Value(section.Value, "Stream"), Website = Ini.Value(section.Value, "Website") };
            s.Validate();
            result.Add(s);
        }
        return result;
    }

    internal static bool ValidId(string id) => id.Length > 0 && id.Length <= 100 && id.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.');
    internal static bool SingleLine(string value, int maximum) => value.Length <= maximum && !value.Any(c => char.IsControl(c) || c == '\u2028' || c == '\u2029');
    internal void Validate()
    {
        if (!ValidId(Id) || string.IsNullOrWhiteSpace(Name) || !SingleLine(Name, 160) || !SingleLine(Region, 80)
            || !SingleLine(Category, 80) || !SingleLine(Frequency, 80) || !SingleLine(Website, 2048)
            || !SingleLine(Stream, 2048) || !Uri.TryCreate(Stream, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("Enter a station name and a direct HTTP(S) audio URL. Keep each field on one line; URLs cannot contain a username or password.");
    }

    internal bool Matches(string category, string region, string search)
        => (category == "All categories" || Category == category) && (region == "All Japan" || region == "All regions" || Region == region)
            && (Name + " " + Detail).IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
}

internal static class MusicLibrary
{
    internal static List<string> Scan(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
        .Where(p => string.Equals(Path.GetExtension(p), ".mp3", StringComparison.OrdinalIgnoreCase))
        .Take(10000).OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();

    internal static int Next(int current, int count, bool shuffle, bool repeat, Random random)
    {
        if (count == 0) return -1;
        if (current < 0 || current >= count) return shuffle ? random.Next(count) : 0;
        if (shuffle && count > 1) { int next = random.Next(count - 1); return next >= current ? next + 1 : next; }
        return current + 1 < count ? current + 1 : repeat ? 0 : -1;
    }
}

internal sealed class MusicOrder
{
    private readonly HashSet<int> _played = new();
    internal void Select(int index) { _played.Clear(); if (index >= 0) _played.Add(index); }
    internal int Next(int current, int count, bool shuffle, bool repeat, Random random)
    {
        if (count == 0) return -1;
        if (current >= 0 && current < count) _played.Add(current);
        if (!shuffle) return MusicLibrary.Next(current, count, false, repeat, random);
        var eligible = Enumerable.Range(0, count).Where(n => !_played.Contains(n)).ToList();
        if (eligible.Count == 0)
        {
            if (!repeat) return -1;
            _played.Clear();
            eligible = Enumerable.Range(0, count).Where(n => count == 1 || n != current).ToList();
        }
        int next = eligible[random.Next(eligible.Count)]; _played.Add(next); return next;
    }
}
