using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RadioRamen;

internal sealed class CompatibilityReport
{
    internal readonly List<string> Hooks = new();
    internal int GameApiReferences;
    internal string Summary => Hooks.Count + "/" + GameCompatibility.ExpectedHooks + " radio hooks; " + GameApiReferences + " game API references; native power and menu contracts";
}

// Game versions and fingerprints are diagnostic only. Validate the interfaces
// this DLL actually calls, plus the private members and behavior its hooks need.
internal static class GameCompatibility
{
    internal const int ExpectedHooks = 7;
    private const string Radio = "Runtime_Classes.Environments.RuntimeEnvironment_Radio";
    private const string Info = "Interactable_Infos.Sealed_SO_Classes.Environment.EnvironmentInfo_Radio";
    private const string UI = "RSServiceLocator.Scripts.UIService";
    private const string Notebook = "RSServiceLocator.Scripts.NotebookService";

    internal static CompatibilityReport Inspect(string gameAssembly, string pluginAssembly)
    {
        using var game = AssemblyDefinition.ReadAssembly(gameAssembly);
        using var plugin = AssemblyDefinition.ReadAssembly(pluginAssembly);
        return Inspect(game, plugin);
    }

    internal static CompatibilityReport Inspect(AssemblyDefinition game, AssemblyDefinition plugin)
    {
        var module = game.MainModule;
        var report = new CompatibilityReport();
        report.GameApiReferences = GameReferences(module, plugin.MainModule);
        Field(module, Radio, "_isOn", "System.Boolean", false, true);
        Field(module, Radio, "CurrentSong", "Sonity.SoundEvent");
        Field(module, UI, "_nonUIToolkitBGBlur", "UnityEngine.GameObject");
        Field(module, Notebook, "instructionsUIAsset", "UnityEngine.UIElements.VisualTreeAsset");
        Field(module, Notebook, "m_panelSettings", "UnityEngine.UIElements.PanelSettings");
        Field(module, Notebook, "m_NotebookUIDoc", "UnityEngine.UIElements.UIDocument");
        Method(module, UI, "get_mouseoverBodyLabel", "UnityEngine.UIElements.Label", true);
        Enum(module, "Managers___Handlers.State", "none", 0);
        Enum(module, "Managers___Handlers.State", "creation", 1);
        Enum(module, "Managers___Handlers.State", "menu", 16);
        Enum(module, "RSServiceLocator.Scripts.MouseOverInfoState", "Override", -1);
        Enum(module, "RSServiceLocator.Scripts.MouseOverInfoState", "Interact", 4);

        var power = Method(module, Info, "InteractEnvironment", "System.Void", false, "UnityEngine.Transform");
        Calls(power, Info, "PlayRadio", 1);
        report.Hooks.Add(power.FullName);
        var toggle = Method(module, Info, "PlayRadio", "System.Void");
        var code = toggle.Body.Instructions;
        int inversions = 0;
        for (int i = 0; i + 3 < code.Count; i++)
            if (IsCall(code[i], Radio, "get_isOn") && code[i + 1].OpCode == OpCodes.Ldc_I4_0
                && code[i + 2].OpCode == OpCodes.Ceq && IsCall(code[i + 3], Radio, "set_isOn")) inversions++;
        Require(inversions == 1, "native radio power no longer toggles its boolean state");
        var channel = Method(module, Info, "Interact", "System.Void", false,
            "Interactable_Infos.Common.MainInteractableInfo", "Interactable_Infos.Common.MainInteractableInfo");
        Calls(channel, Radio, "SwitchStation", 1);
        report.Hooks.Add(channel.FullName);

        var getter = Method(module, Radio, "get_isOn", "System.Boolean");
        var read = getter.Body.Instructions.Where(i => i.OpCode != OpCodes.Nop).ToArray();
        Require(read.Length == 3 && read[0].OpCode == OpCodes.Ldarg_0 && read[1].OpCode == OpCodes.Ldfld
            && IsField(read[1], Radio, "_isOn") && read[2].OpCode == OpCodes.Ret, "native radio power getter no longer returns the hooked state directly");
        var setter = Method(module, Radio, "set_isOn", "System.Void", false, "System.Boolean");
        Require(setter.Body.Instructions.Count(i => i.OpCode == OpCodes.Stfld && IsField(i, Radio, "_isOn")) == 1,
            "native radio setter state write changed");
        Calls(setter, Radio, "ToggleRadio", 1);
        report.Hooks.Add(setter.FullName);
        var visual = Method(module, Radio, "ToggleRadio", "System.Void");
        Require(!visual.Body.Instructions.Any(i => ((i.OpCode == OpCodes.Stfld || i.OpCode == OpCodes.Ldflda) && (IsField(i, Radio, "_isOn") || IsField(i, Radio, "CurrentSong")))
            || IsCall(i, Radio, "set_isOn") || (i.Operand is MethodReference m && m.DeclaringType.Namespace == "Sonity" && m.Name is "Play" or "Stop")),
            "radio visual update now changes power or native playback");

        var disable = Method(module, Radio, "OnDisable", "System.Void");
        Calls(disable, Radio, "set_isOn", 1);
        int off = disable.Body.Instructions.ToList().FindIndex(i => IsCall(i, Radio, "set_isOn"));
        Require(off > 0 && disable.Body.Instructions[off - 1].OpCode == OpCodes.Ldc_I4_0, "radio removal no longer powers it off");
        report.Hooks.Add(disable.FullName);
        report.Hooks.Add(Method(module, "Managers___Handlers.PauseMenuManager", "TogglePauseMenu", "System.Void").FullName);
        report.Hooks.Add(Method(module, UI, "ShowMouseoverUI", "System.Void", false, "Runtime_Classes.Common.RuntimeInteractable", "System.String").FullName);
        report.Hooks.Add(Method(module, UI, "SetCrosshairHintText", "System.Void", false, "RSServiceLocator.Scripts.MouseOverInfoState", "System.String").FullName);
        Require(report.Hooks.Count == ExpectedHooks, "incomplete radio hook preflight");
        return report;
    }

    private static int GameReferences(ModuleDefinition game, ModuleDefinition plugin)
    {
        int count = 0;
        foreach (var type in plugin.GetTypeReferences().Where(t => Scope(t) == "Assembly-CSharp"))
        { Type(game, type.GetElementType().FullName); count++; }
        foreach (var reference in plugin.GetMemberReferences().Where(m => Scope(m.DeclaringType) == "Assembly-CSharp"))
        {
            var type = Type(game, reference.DeclaringType.GetElementType().FullName);
            if (reference is MethodReference original)
            {
                var method = original.GetElementMethod();
                var matches = Hierarchy(game, type).SelectMany(t => t.Methods).Where(m => m.Name == method.Name
                    && m.IsStatic == !method.HasThis && m.GenericParameters.Count == method.GenericParameters.Count
                    && Signature(m.ReturnType) == Signature(method.ReturnType)
                    && m.Parameters.Select(p => Signature(p.ParameterType)).SequenceEqual(method.Parameters.Select(p => Signature(p.ParameterType)))).ToArray();
                Require(matches.Length == 1 && matches[0].IsPublic, "game API method changed: " + original.FullName);
            }
            else if (reference is FieldReference field)
            {
                var matches = Hierarchy(game, type).SelectMany(t => t.Fields).Where(f => f.Name == field.Name && Signature(f.FieldType) == Signature(field.FieldType)).ToArray();
                Require(matches.Length == 1 && matches[0].IsPublic, "game API field changed: " + field.FullName);
                foreach (var instruction in AllTypes(plugin.Types).SelectMany(t => t.Methods).Where(m => m.HasBody)
                    .SelectMany(m => m.Body.Instructions).Where(i => i.Operand is FieldReference f && f.FullName == field.FullName))
                {
                    bool staticUse = instruction.OpCode.Code is Code.Ldsfld or Code.Ldsflda or Code.Stsfld;
                    Require(matches[0].IsStatic == staticUse, "game API field changed between instance and static: " + field.FullName);
                    if (instruction.OpCode.Code is Code.Stfld or Code.Stsfld)
                        Require(!matches[0].IsInitOnly && !matches[0].IsLiteral, "game API field became read-only: " + field.FullName);
                }
            }
            count++;
        }
        return count;
    }

    private static string Signature(TypeReference type)
    {
        if (type is GenericParameter generic) return (generic.Type == GenericParameterType.Method ? "!!" : "!") + generic.Position;
        if (type is GenericInstanceType instance) return instance.ElementType.FullName + "<" + string.Join(",", instance.GenericArguments.Select(Signature)) + ">";
        if (type is ByReferenceType byReference) return Signature(byReference.ElementType) + "&";
        if (type is ArrayType array) return Signature(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
        return type.FullName;
    }

    private static string Scope(TypeReference type) => type.GetElementType().Scope is AssemblyNameReference assembly ? assembly.Name
        : type.GetElementType().Scope is ModuleDefinition module ? module.Assembly.Name.Name : "";
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(AllTypes(t.NestedTypes)));
    private static IEnumerable<TypeDefinition> Hierarchy(ModuleDefinition module, TypeDefinition type)
    {
        var visited = new HashSet<string>();
        while (visited.Add(type.FullName))
        {
            yield return type;
            if (type.BaseType == null || Scope(type.BaseType) != "Assembly-CSharp") yield break;
            type = Type(module, type.BaseType.GetElementType().FullName);
        }
    }
    private static TypeDefinition Type(ModuleDefinition module, string name)
    {
        var type = module.GetType(name);
        if (type == null || type.FullName != name) throw Failure("missing game type " + name);
        return type;
    }
    private static void Field(ModuleDefinition module, string type, string name, string expected, bool isStatic = false, bool writable = false)
    {
        var fields = Type(module, type).Fields.Where(f => f.Name == name).ToArray();
        Require(fields.Length == 1 && fields[0].FieldType.FullName == expected && fields[0].IsStatic == isStatic
            && (!writable || (!fields[0].IsInitOnly && !fields[0].IsLiteral)), "private field contract changed: " + type + "." + name);
    }
    private static MethodDefinition Method(ModuleDefinition module, string type, string name, string result, bool isStatic = false, params string[] parameters)
    {
        var methods = Type(module, type).Methods.Where(m => m.Name == name && m.ReturnType.FullName == result && m.IsStatic == isStatic
            && !m.HasGenericParameters && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters)).ToArray();
        Require(methods.Length == 1 && methods[0].HasBody,
            "method contract is missing or ambiguous: " + type + "." + name);
        return methods[0];
    }
    private static void Enum(ModuleDefinition module, string type, string name, int value)
    {
        var enumeration = Type(module, type);
        var field = enumeration.Fields.SingleOrDefault(f => f.Name == name);
        Require(enumeration.IsEnum && enumeration.Fields.Any(f => f.Name == "value__" && f.FieldType.FullName == "System.Int32")
            && field != null && field.HasConstant && Convert.ToInt32(field.Constant) == value, "player state value changed: " + name);
    }
    private static void Calls(MethodDefinition method, string type, string name, int count)
        => Require(method.Body.Instructions.Count(i => IsCall(i, type, name)) == count, "native call changed in " + method.FullName + ": " + name);
    private static bool IsCall(Instruction instruction, string type, string name)
        => (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) && instruction.Operand is MethodReference m && m.Name == name && m.DeclaringType.FullName == type;
    private static bool IsField(Instruction instruction, string type, string name)
        => instruction.Operand is FieldReference f && f.Name == name && f.DeclaringType.FullName == type;
    private static void Require(bool condition, string reason) { if (!condition) throw Failure(reason); }
    private static InvalidDataException Failure(string reason) => new("Radio compatibility check: " + reason + ". Original radio controls retained.");
}
