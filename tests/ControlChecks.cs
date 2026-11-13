using Mono.Cecil;
using RadioRamen;

internal static class ControlChecks
{
    internal static void Run(string module, Action<bool, string> check)
    {
        string expected = "Press " + RadioPromptText.InteractKey + " to interact\nPress " + RadioPromptText.LeftMouse + " to turn on or off";
        foreach (string e in new[] { "<sprite=\"spritesheet_desk\" name=\"e\">", "<sprite name=E>", "<sprite name='e'>", "E" })
        foreach (string mouse in new[] { "<sprite=\"spritesheet_desk\" name=\"ml\">", "<sprite name=LeftMouse>", "LMB" })
            check(RadioPromptText.Controls("Press " + mouse + " to change channel\nPress " + e + " to turn on or off") == expected,
                "hover prompt swaps both controls exactly once: " + mouse + " / " + e);
        check(RadioPromptText.Controls("Press LMB to Change the Channel") == "Press " + RadioPromptText.InteractKey + " to interact",
            "the alternate channel phrase uses E and interact wording");
        check(RadioPromptText.Controls("Press <sprite name=E> to clean") == "Press <sprite name=E> to clean",
            "unrelated hover wording is preserved");
        using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(module, "src/bin/Release/netstandard2.1/RadioRamen.dll"));
        var plugin = assembly.MainModule.GetType("RadioRamen.Plugin");
        bool Calls(MethodDefinition method, string type, string name) => method.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == type && m.Name == name);
        foreach (var (hook, native, target) in new[] {
            ("RadioMenuInteraction", "InteractEnvironment", "Open"), ("RadioPowerInteraction", "Interact", "TogglePower") })
        {
            var type = assembly.MainModule.GetType("RadioRamen." + hook);
            check(type.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"
                    && a.ConstructorArguments.Any(v => v.Value is string s && s == native))
                && Calls(type.Methods.Single(m => m.Name == "Prefix"), "RadioRamen.Plugin", target),
                "native " + native + " routes to " + target);
        }
        var panel = assembly.MainModule.GetType("RadioRamen.RadioPanel");
        var status = panel.Methods.Single(m => m.Name == "UpdateStatus");
        check(Calls(plugin.Methods.Single(m => m.Name == "get_IsPowered"), "Runtime_Classes.Environments.RuntimeEnvironment_Radio", "get_isOn")
            && Calls(status, "RadioRamen.Plugin", "get_IsPowered")
            && new[] { "Power on", "Power off" }.All(text => status.Body.Instructions.Any(i => Equals(i.Operand, text))),
            "the menu power label follows the radio's real saved power flag");
        IEnumerable<TypeDefinition> Nested(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Nested));
        check(Nested(panel).SelectMany(t => t.Methods).Where(m => m.HasBody).Any(m => m.Body.Instructions.Any(i => i.Operand is MethodReference r
                && r.DeclaringType.FullName == "RadioRamen.Plugin" && r.Name == "TogglePower" && r.Parameters.Count == 0)),
            "the panel's power button invokes the bidirectional attached-radio control");
        var toggle = plugin.Methods.Single(m => m.Name == "TogglePower" && m.Parameters.Count == 0);
        check(Calls(toggle, "RadioRamen.Plugin", "PowerOff") && Calls(toggle, "Runtime_Classes.Environments.RuntimeEnvironment_Radio", "set_isOn")
            && Calls(toggle, "RadioRamen.Plugin", "PlaySelected") && !Calls(toggle, "RadioRamen.Decoder", "Play"),
            "the shared switch can power off, resume the attached MP3, or choose a remembered source");
        var open = plugin.Methods.Single(m => m.Name == "Open");
        check(Calls(open, "RSServiceLocator.Scripts.UIService", "ToggleUIDoc") && Calls(open, "RSServiceLocator.Scripts.UIService", "RemoveMouseOverUI")
            && !open.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "UnityEngine.UIElements.IStyle"),
            "opening the radio uses native HUD and hover transitions without instant style overrides");
        check(Calls(plugin.Methods.Single(m => m.Name == "RestorePanelState"), "Managers___Handlers.PlayerState", "SetState")
            && !assembly.MainModule.Types.Any(t => t.Name == "RadioCrosshairPrompt"),
            "closing restores native player HUD state and the original E crosshair prompt needs no override");
    }
}
