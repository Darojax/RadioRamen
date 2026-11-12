using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace RadioRamen;

internal sealed class StartupBadge
{
    internal const string RelaxedId = "com.danie.ramenshop.relaxed";
    private readonly GUIContent _text;
    private GUIStyle? _style, _shadow;
    private BaseUnityPlugin? _relaxed;
    private FieldInfo? _relaxedActive;
    private bool _peerChecked;
    private float _shownAt = -1f;
    private bool _expired;

    internal StartupBadge(string version) { _text = new GUIContent("RadioRamen v" + version); }

    internal void Draw()
    {
        if (_expired || Event.current.type != EventType.Repaint) return;
        float now = Time.realtimeSinceStartup;
        if (_shownAt < 0) _shownAt = now;
        if (now - _shownAt >= 3f) { _expired = true; return; }
        if (!_peerChecked)
        {
            _peerChecked = true;
            if (Chainloader.PluginInfos.TryGetValue(RelaxedId, out var info) && info.Instance)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var type = info.Instance.GetType();
                var active = type.GetField("_active", flags);
                // Older RelaxedRamen releases have no version label. Read the
                // optional peer without linking its DLL or changing its state.
                if (type.GetMethod("OnGUI", flags) != null && active?.FieldType == typeof(bool))
                { _relaxed = info.Instance; _relaxedActive = active; }
            }
        }
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.LowerLeft };
            _style.normal.textColor = new Color(0.96f, 0.94f, 0.87f, 0.85f);
            _shadow = new GUIStyle(_style);
            _shadow.normal.textColor = new Color(0, 0, 0, 0.75f);
        }
        bool aboveRelaxed = _relaxed && _relaxed.isActiveAndEnabled && _relaxedActive?.GetValue(_relaxed) is true;
        var rect = new Rect(12, Screen.height - 34 - (aboveRelaxed ? 22 : 0), 240, 24);
        GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), _text, _shadow);
        GUI.Label(rect, _text, _style);
    }
}
