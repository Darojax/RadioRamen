using System;
using System.Collections.Generic;
using HarmonyLib;
using Runtime_Classes.Common;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RadioRamen;

// Use the native indoor volumes and identifiable restaurant entrances, rather
// than interpreting placement/interaction colliders as acoustic wall material.
internal sealed class RoomAcoustics : IDisposable
{
    private readonly Transform _radio;
    private readonly Vector3 _localCentre;
    private readonly List<BoxCollider> _rooms = new();
    private readonly List<Entrance> _entrances = new();
    private readonly List<RoomPoint> _openings = new();
    private readonly RoomRoute _route = new();
    internal readonly RoomSoundFade Fade = new();
    private AudioListener? _listener;
    private bool _dirty = true, _disposed;
    private float _nextGeometry, _nextListener, _nextMeasure, _target, _targetGain=1;
    internal string Diagnostic { get; private set; } = "Waiting for Nearby playback";
    internal string Measurement { get; private set; } = "Not measured";
    internal string Snapshot => Diagnostic + "; " + Measurement + "; wall mix " + Fade.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        + "; gain " + Gain.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "; cutoff " + Cutoff.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " Hz";
    internal float Gain => Fade.Gain;
    internal float Cutoff => Fade.Cutoff;

    private readonly struct Entrance
    {
        internal readonly Transform Root;
        internal readonly BoxCollider Left, Right;
        internal readonly Vector3 LocalCentre, LocalHalfSpan;
        internal Entrance(Transform root, BoxCollider left, BoxCollider right, Vector3 centre, Vector3 span)
        { Root = root; Left = left; Right = right; LocalCentre = centre; LocalHalfSpan = span; }
    }

    internal RoomAcoustics(Transform radio)
    {
        _radio = radio;
        var renderers = radio.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            _localCentre = radio.InverseTransformPoint(bounds.center);
        }
        SceneManager.sceneLoaded += Loaded; SceneManager.sceneUnloaded += Unloaded;
    }

    private void Loaded(Scene scene, LoadSceneMode mode) { _dirty = true; _nextGeometry = 0; }
    private void Unloaded(Scene scene) { _dirty = true; _nextGeometry = 0; }

    internal void Update(bool nearby, bool active, int volume)
    {
        if (!nearby) { Fade.Reset(); _target = 0; _targetGain=1; Diagnostic = "Normal: native distance only"; return; }
        if (_disposed || !_radio || !active) return;
        float now = Time.realtimeSinceStartup;
        if ((!_listener || !_listener.isActiveAndEnabled) && now >= _nextListener)
        {
            _nextListener = now + 1;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (candidate.isActiveAndEnabled) { _listener = candidate; break; }
        }
        if (now >= _nextGeometry && (_dirty || _rooms.Count == 0 || _entrances.Count == 0
            || _rooms.Exists(room => !room) || _entrances.Exists(entrance => !entrance.Root || !entrance.Left || !entrance.Right))) RefreshGeometry(now);
        if (now >= _nextMeasure)
        {
            _nextMeasure = now + .15f;
            _target = Measure(volume);
        }
        Fade.Advance(_target,_targetGain,Time.unscaledDeltaTime);
    }

    private void RefreshGeometry(float now)
    {
        _nextGeometry = now + 5; _dirty = false; _rooms.Clear(); _entrances.Clear();
        var type = AccessTools.TypeByName("Managers___Handlers.Handlers.RestaurantHandler");
        if (type == null || !typeof(Component).IsAssignableFrom(type)) return;
        var owner=AccessTools.Property(type,"Instance")?.GetValue(null,null) as Behaviour;
        if(!owner||!owner.isActiveAndEnabled||!owner.gameObject.scene.isLoaded)return;
        var box=AccessTools.Field(type,"m_restaurantCollider")?.GetValue(owner) as BoxCollider;
        if(!box)box=owner.GetComponent<BoxCollider>();
        if(!box||!box.enabled||!box.gameObject.activeInHierarchy||Vector3.Dot(box.transform.up,Vector3.up)<.99f)return;
        _rooms.Add(box);
        foreach (var item in UnityEngine.Object.FindObjectsByType<RuntimeInteractable>(FindObjectsSortMode.None))
        {
            if (!item || !item.gameObject.activeInHierarchy) continue;
            if(item.gameObject.scene!=owner.gameObject.scene)continue;
            bool entranceMarker = EntranceName(item.name);
            // The game can rename the placed root to its localized display
            // name. Its direct native variant marker remains identifiable.
            for (int i = 0; !entranceMarker && i < item.transform.childCount; i++)
                entranceMarker = EntranceName(item.transform.GetChild(i).name);
            if (!entranceMarker) continue;
            // Confirm the native two sliding leaves. The IO entrance pivot is
            // at the centre of their frame in both reviewed restaurant scenes.
            // Whole-facade renderer bounds can put the opening on a window.
            var leaves = item.GetComponentsInChildren<BoxCollider>(true);
            BoxCollider? left = null, rightLeaf = null;
            Vector3 right = item.transform.right;
            foreach (var leaf in leaves)
            {
                if (leaf.name != "Door" || !leaf.transform.parent
                    || leaf.transform.parent.name is not ("Door_L_Root" or "Door_R_Root")) continue;
                if (leaf.transform.parent.name == "Door_L_Root") left = leaf; else rightLeaf = leaf;
            }
            if (!left || !rightLeaf) continue;
            float widths = LeafWidth(left, right) + LeafWidth(rightLeaf, right);
            if (widths < 1 || widths > 5) continue;
            float half = widths / 2 - .03f;
            _entrances.Add(new Entrance(item.transform, left, rightLeaf, Vector3.zero,
                item.transform.InverseTransformVector(right * half)));
        }
    }

    private float Measure(int volume)
    {
        _targetGain=1;
        if (!_listener || !_listener.isActiveAndEnabled) { Diagnostic = "No active listener: native distance fallback"; return 0; }
        Vector3 source = _radio.TransformPoint(_localCentre), ear = _listener.transform.position;
        Measurement = "rooms " + _rooms.Count + "; entrances " + _entrances.Count + "; listener " + _listener.name
            + "; radio " + source.ToString("F2") + "; ear " + ear.ToString("F2");
        foreach (var box in _rooms)
        {
            if (!box || !box.enabled || !box.gameObject.activeInHierarchy) continue;
            bool sourceInside = Inside(box, source, true);
            bool earInside = Inside(box, ear, false);
            Measurement+="; host "+box.name+"; centre "+box.transform.TransformPoint(box.center).ToString("F2")
                +"; size "+box.size.ToString("F2")+"; source-in "+sourceInside+"; ear-in "+earInside;
            if (sourceInside && earInside) { Diagnostic = "Same native room"; return 0; }
        }
        foreach (var box in _rooms)
        {
            if (!box || !box.enabled || !box.gameObject.activeInHierarchy) continue;
            bool sourceInside = Inside(box, source, true);
            bool earInside = Inside(box, ear, false);
            if (sourceInside == earInside) continue;
            var scale = box.transform.lossyScale;
            var room = RoomFootprint.Restaurant(box.size.x * Math.Abs(scale.x) / 2, box.size.z * Math.Abs(scale.z) / 2);
            RoomPoint Point(Vector3 world)
            {
                Vector3 p = box.transform.InverseTransformPoint(world) - box.center;
                return new RoomPoint(p.x * Math.Abs(scale.x), p.z * Math.Abs(scale.z));
            }
            var rawInside=Point(sourceInside?source:ear);
            var inside = room.InsetSource(rawInside); var outside = Point(sourceInside ? ear : source);
            float direct = Vector3.Distance(source,ear), best = 0, bestDistance = 0, bestGain=-1;
            int confirmed = 0;
            foreach (var entrance in _entrances)
            {
                if (!entrance.Root || !entrance.Left || !entrance.Right || !entrance.Root.gameObject.activeInHierarchy) continue;
                var centre = Point(entrance.Root.TransformPoint(entrance.LocalCentre));
                if (room.BoundaryDistance(centre) > 1f) continue;
                _openings.Clear();
                for (int i = -4; i <= 4; i++)
                    _openings.Add(Point(entrance.Root.TransformPoint(entrance.LocalCentre + entrance.LocalHalfSpan * (i / 4f))));
                // These sliding leaves have wide air gaps. Their position must
                // not add a closed-door penalty just outside the room boundary.
                float horizontal = _route.ThroughEntrance(room, inside, outside, _openings)+RoomPoint.Distance(rawInside,inside);
                float distance = Math.Max(direct,(float)Math.Sqrt(horizontal*horizontal+(source.y-ear.y)*(source.y-ear.y)));
                if (float.IsNaN(distance) || float.IsInfinity(distance)) continue;
                float target = RoomSoundFade.Target(direct, distance);
                float gain=RoomSoundFade.RouteGain(volume,direct,distance);
                if (gain>bestGain) { best = target; bestDistance = distance;bestGain=gain; }
                confirmed++;
            }
            if (confirmed == 0) continue;
            Diagnostic = "Native room/entrance route; " + (bestDistance - direct).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                + "m detour; route "+bestDistance.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+"m; " + confirmed + " entrances";
            _targetGain=bestGain;
            return best;
        }
        Diagnostic = "Unconfirmed room/entrance: native distance fallback"; return 0;
    }

    private static bool Inside(BoxCollider box, Vector3 world, bool source)
    {
        var p=box.transform.InverseTransformPoint(world)-box.center;var scale=box.transform.lossyScale;
        // Indoor triggers are inset from the physical walls and can end below
        // a shelf-mounted radio. Use the horizontal acoustic footprint and a
        // modest source-only wall allowance; the listener boundary stays exact.
        if(Math.Abs(p.y*scale.y)>box.size.y*Math.Abs(scale.y)/2+.75f)return false;
        var room=RoomFootprint.Restaurant(box.size.x*Math.Abs(scale.x)/2,box.size.z*Math.Abs(scale.z)/2);
        var point=new RoomPoint(p.x*Math.Abs(scale.x),p.z*Math.Abs(scale.z));
        return source?room.ContainsSource(point):room.Contains(point);
    }

    private static float LeafWidth(BoxCollider leaf, Vector3 right)
    {
        Vector3 size = leaf.size, scale = leaf.transform.lossyScale;
        return Math.Abs(Vector3.Dot(right, leaf.transform.right) * size.x * scale.x)
            + Math.Abs(Vector3.Dot(right, leaf.transform.forward) * size.z * scale.z);
    }

    private static bool EntranceName(string name) => name.IndexOf("entrance", StringComparison.OrdinalIgnoreCase) >= 0
        && name.IndexOf("restaurant", StringComparison.OrdinalIgnoreCase) >= 0
        && name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) < 0
        && name.IndexOf("ceiling", StringComparison.OrdinalIgnoreCase) < 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; SceneManager.sceneLoaded -= Loaded; SceneManager.sceneUnloaded -= Unloaded;
        _rooms.Clear(); _entrances.Clear(); _listener = null;
    }
}
