using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RadioRamen;

// Created by player actions; never shipped as a package default.
internal sealed class UserStationLibrary
{
    internal const string FileName = "UserStations.ini";
    internal readonly List<Station> Stations = new();
    internal readonly HashSet<string> Favourites = new(StringComparer.OrdinalIgnoreCase);

    internal static UserStationLibrary Load(string path)
    {
        if (!File.Exists(path)) return new UserStationLibrary();
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("The user station library is larger than 1 MB.");
        return Parse(File.ReadAllText(path));
    }

    internal static UserStationLibrary Parse(string text)
    {
        var result = new UserStationLibrary();
        var sections = Ini.Read(text);
        if (sections.TryGetValue("Favourites", out var favourites))
        {
            foreach (var entry in favourites)
            {
                if (!Station.ValidId(entry.Key) || !bool.TryParse(entry.Value, out bool enabled))
                    throw new InvalidDataException("Invalid favourite station entry.");
                if (enabled) result.Favourites.Add(entry.Key);
            }
            sections.Remove("Favourites");
        }
        result.Stations.AddRange(Station.ParseSections(sections));
        foreach (var station in result.Stations) station.IsCustom = true;
        result.Validate(); return result;
    }

    internal static List<Station> Merge(IEnumerable<Station> bundled, UserStationLibrary user)
    {
        var result = bundled.Concat(user.Stations).ToList();
        if (result.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
            throw new InvalidDataException("A custom station ID conflicts with another station.");
        return result;
    }

    internal Station Upsert(string? id, string name, string stream, string category, string region, IEnumerable<Station> bundled)
    {
        if (id != null && !Stations.Any(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("This custom station changed or was removed. Refresh the list before editing it.");
        var previous = id == null ? null : Stations.First(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var candidate = new Station { Id = id ?? "custom-" + Guid.NewGuid().ToString("N"), Name = name.Trim(), Stream = stream.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? "My stations" : category.Trim(), Region = string.IsNullOrWhiteSpace(region) ? "Online" : region.Trim(),
            Frequency = "Internet radio", Website = previous?.Website ?? "", IsCustom = true };
        candidate.Validate();
        if (bundled.Any(s => s.Id.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A custom station cannot replace a bundled station.");
        int at = id == null ? -1 : Stations.FindIndex(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (at < 0 && Stations.Count >= 500) throw new InvalidDataException("The user library supports up to 500 custom stations.");
        if (at >= 0) Stations[at] = candidate; else Stations.Add(candidate);
        return candidate;
    }

    internal void SetFavourite(string id, bool favourite)
    {
        if (!Station.ValidId(id)) throw new InvalidDataException("Invalid favourite station ID.");
        if (favourite) Favourites.Add(id); else Favourites.Remove(id);
    }

    internal void Remove(string id)
    {
        int at = Stations.FindIndex(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (at < 0) throw new InvalidDataException("Only a saved custom station can be removed here.");
        Stations.RemoveAt(at); Favourites.Remove(id);
    }

    private void Validate()
    {
        if (Stations.Count > 500 || Favourites.Count > 5000) throw new InvalidDataException("The user station library has too many entries.");
        if (Stations.Any(s => !s.Id.StartsWith("custom-", StringComparison.OrdinalIgnoreCase))
            || Stations.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Stations.Count)
            throw new InvalidDataException("Custom station IDs must be unique and begin with custom-.");
        foreach (var station in Stations) station.Validate();
        if (Favourites.Any(id => !Station.ValidId(id))) throw new InvalidDataException("Invalid favourite station ID.");
    }

    internal void Save(string path)
    {
        Validate();
        var text = new StringBuilder("; RadioRamen custom stations and favourites.\n; Kept separately from the bundled station catalogue.\n\n[Favourites]\n");
        foreach (string id in Favourites.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)) text.Append(id).AppendLine(" = true");
        foreach (var station in Stations)
        {
            text.Append("\n[").Append(station.Id).AppendLine("]");
            foreach (var value in new[] { ("Name", station.Name), ("Region", station.Region), ("Category", station.Category),
                ("Frequency", "Internet radio"), ("Stream", station.Stream), ("Website", station.Website) })
                text.Append(value.Item1).Append(" = ").AppendLine(value.Item2);
        }
        string content = text.ToString();
        if (Encoding.UTF8.GetByteCount(content) > 1024 * 1024) throw new InvalidDataException("The user station library is larger than 1 MB.");
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak"); else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
