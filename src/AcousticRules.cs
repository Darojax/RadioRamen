using System;

namespace RadioRamen;

internal static class AcousticRules
{
    internal const float ProbeInterval = .2f, CloseSeconds = .12f, OpenSeconds = .25f;

    internal static bool Blocks(string owner, string path, string layer, bool interactable, bool hasRigidbody, float x, float y, float z)
    {
        // These are native interaction/placement helpers, not solid surfaces.
        if (layer is "Raycast Only" or "PlaceableSurface" or "PlacementCollision" or "TempPlacementCollision"
            or "Player" or "UI" or "Inventory" or "LightZone" or "PlayerEntersTrigger" or "Creation" or "ShelfTargeting") return false;
        float longest = Math.Max(x, Math.Max(y, z));
        float middle = x + y + z - longest - Math.Min(x, Math.Min(y, z));
        if (longest < .75f || middle < .04f) return false;
        if (interactable)
        {
            // Shop walls are themselves movable decorations. Inspect their
            // nearest owner before excluding ordinary movable furniture.
            if (!Starts(owner, "IO_Wall", "IO_Floor", "IO_Ceiling", "IO_Facade", "IO_Door", "IO_Window")
                && !(Has(owner, "restaurant") && Has(owner, "entrance"))) return false;
        }
        else if (hasRigidbody) return false;
        // Doorway click boxes can span the open aperture. Physical jambs and
        // the actual moving door leaf still participate.
        if (layer == "Interactible" && Has(path, "doorway") && !path.EndsWith("/Door", StringComparison.OrdinalIgnoreCase)) return false;
        if (interactable || Has(path, "wall", "facade", "building", "ceiling", "floor", "roof", "door", "blockers")) return true;
        // Some street/building colliders are just named Cube. Use the large,
        // immovable geometry only; narrow props cannot silence the whole radio.
        return longest >= 1.8f && middle >= .6f;
    }

    private static bool Has(string text, params string[] words)
    {
        foreach (string word in words) if (text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
    private static bool Starts(string text, params string[] words)
    {
        foreach (string word in words) if (text.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

internal sealed class ObstructionFade
{
    internal float Amount { get; private set; }
    internal float Gain => (1 - Amount) * (1 - Amount);
    internal float Cutoff => (float)(22000 * Math.Pow(900d / 22000, Amount));
    internal void Reset() => Amount = 0;
    internal void Advance(int blocked, int paths, float seconds)
    {
        if (paths <= 0 || blocked < 0 || blocked > paths || seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds))
            throw new ArgumentOutOfRangeException(nameof(paths));
        float target = blocked / (float)paths;
        float step = seconds / (target > Amount ? AcousticRules.CloseSeconds : AcousticRules.OpenSeconds);
        Amount = target > Amount ? Math.Min(target, Amount + step) : Math.Max(target, Amount - step);
    }
}
