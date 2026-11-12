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
        }
        check(Near(RadioRange.Gain(true, 10, 6), 0) && RadioRange.Gain(true, 50, 6) > 0 && RadioRange.Gain(true, 100, 6) > RadioRange.Gain(true, 50, 6),
            "at the same outside distance a low-volume radio is silent while louder radios carry farther");
        check(RadioRange.Gain(false, 50, 29) > 0 && RadioRange.Gain(false, 50, 30) == 0, "Normal no longer has a logarithmic tail beyond its intended limit");

        bool Blocks(string owner, string path, string layer, bool item = true, bool rigid = false, float x = 1, float y = 2.5f, float z = .125f)
            => AcousticRules.Blocks(owner, path, layer, item, rigid, x, y, z);
        // Dimensions/names verified against the installed Restaurant001/002 and
        // Alley001 colliders. No copied game binaries are needed for these cases.
        check(Blocks("IO_Wall003_1m_2", "IO_Wall003_1m_2/Wall/Physics Collision", "Physics", x: .99f, y: 2.4745f, z: .12375f),
            "real movable shop-wall geometry still blocks sound");
        check(Blocks("IO_Ceiling001_Restaurant001_Kitchen", "Ceiling/Physics Collision", "Physics", x: 3.09375f, y: .061875f, z: 4.26937f), "native ceiling panels block sound despite being thin");
        check(Blocks("", "Buildings/Building008/Base/Cube_8", "PlayerCollision", false, x: 4, y: 3, z: .2f), "concrete building walls block the route across the other side of a building");
        check(Blocks("", "World/Cube_11", "Default", false, x: 3, y: 2.5f, z: .2f), "unnamed immovable building geometry has a size fallback");
        check(Blocks("IO_Restaurant001_Entrance001", "IO_Restaurant001_Entrance001/Door_R_Root/Door", "Interactible", x: .87885f, y: 1.96492f, z: .05795f), "the actual moving entrance door leaf can block a closed doorway");
        check(!Blocks("IO_Wall001_Doorway001_3m", "IO_Wall001_Doorway001_3m/Collision/IC", "Interactible"), "doorway interaction volumes do not fill open doorways acoustically");
        check(Blocks("IO_Wall001_Doorway001_3m", "IO_Wall001_Doorway001_3m/Collision/FC", "Physics"), "physical doorway jambs remain valid obstructions");
        check(!Blocks("IO_WorkBench001", "IO_WorkBench001/DoorParent/Door", "Interactible")
            && !Blocks("IO_Fridge002", "Building/IO_Fridge002/Door001/Plane", "Default", x: 2, y: 3, z: 1), "cabinet and fridge doors cannot silence the room as structural walls");
        check(!Blocks("IO_Clock_Wall001", "IO_Wall001/IO_Clock_Wall001", "Interactible", x: .3f, y: .3f, z: .1f), "wall-mounted decorations are not mistaken for the wall behind them");
        check(!Blocks("IO_Office_Lamp_Ceiling001", "Ceiling/Lamp/Interactable Collision", "Interactible", x: .2356f, y: .553f, z: .2356f), "ceiling lamps do not behave like ceilings");
        foreach (string helper in new[] { "Raycast Only", "PlaceableSurface", "PlacementCollision", "TempPlacementCollision", "Player", "Inventory", "LightZone" })
            check(!Blocks("IO_Wall001", "IO_Wall001/Collision", helper), "native helper layer is ignored: " + helper);
        check(!Blocks("", "Cart", "Physics", false, true, 3, 2, 1) && !Blocks("IO_Table001", "Building/IO_Table001", "Physics"), "unrelated moving objects and furniture do not mute the radio");

        var fade = new ObstructionFade();
        check(fade.Gain == 1 && fade.Cutoff == 22000, "clear paths have original gain and clean bandwidth");
        fade.Advance(3, 3, AcousticRules.CloseSeconds / 2);
        check(fade.Amount > 0 && fade.Amount < 1 && fade.Gain > 0 && fade.Gain < 1, "walking behind a wall fades smoothly instead of clicking");
        fade.Advance(3, 3, AcousticRules.CloseSeconds);
        check(fade.Gain == 0 && Near(fade.Cutoff, 900), "three fully blocked paths reach exact silence and a muffled filter");
        fade.Advance(1, 3, 1);
        check(fade.Gain > .4f && fade.Gain < .5f && fade.Cutoff > 900 && fade.Cutoff < 22000, "partly open routes retain quiet muffled sound");
        fade.Advance(0, 3, AcousticRules.OpenSeconds);
        check(fade.Gain == 1 && fade.Cutoff == 22000, "an open doorway restores the original sound");
        fade.Advance(3, 3, 1); fade.Reset();
        check(fade.Gain == 1 && fade.Amount == 0, "returning to Normal clears the wall filter and gain reduction");
        fade.Advance(3, 3, 10);
        check(fade.Amount == 1 && fade.Gain == 0, "long frames cannot overshoot the wall gain");

        var first = TuningSignal.Create(123); var repeat = TuningSignal.Create(123); var different = TuningSignal.Create(456);
        check(first.SequenceEqual(repeat) && !first.SequenceEqual(different), "tuning noise is reproducible for checks and supports different textures");
        check(TuningSignal.Create(0).Any(v => v != 0), "a zero random seed cannot make the tuning effect silent");
        var rms = Enumerable.Range(0, TuningSignal.Seconds * 4).Select(k => Math.Sqrt(first.Skip(k * TuningSignal.Frequency / 4).Take(TuningSignal.Frequency / 4).Average(x => (double)x * x))).ToArray();
        check(rms.Max() > rms.Min() * 1.5, "the dial sweep has meaningful squelch dips and changing static intensity");
        check(first.Zip(first.Skip(1)).Max(p => Math.Abs(p.First - p.Second)) < .5, "analogue tuning has no unbounded sample discontinuities");

        // A listenable offline artifact; never played automatically or installed.
        string path = Path.Combine(module, "artifacts/tuning-0.2.0.wav");
        using var output = new BinaryWriter(File.Create(path));
        output.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); output.Write(36 + first.Length * 2);
        output.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); output.Write(16); output.Write((short)1); output.Write((short)1);
        output.Write(TuningSignal.Frequency); output.Write(TuningSignal.Frequency * 2); output.Write((short)2); output.Write((short)16);
        output.Write(System.Text.Encoding.ASCII.GetBytes("data")); output.Write(first.Length * 2);
        foreach (float sample in first) output.Write((short)Math.Round(sample * TuningSignal.Volume * short.MaxValue));
    }
}
