using System;
using System.Collections.Generic;
using Runtime_Classes.Common;
using UnityEngine;

namespace RadioRamen;

// Main-thread, bounded scene queries. No mixer/global physics settings change.
internal sealed class RadioObstruction
{
    private readonly Transform _radio;
    private readonly Vector3 _emitterOffset;
    private readonly RaycastHit[] _hits = new RaycastHit[32], _denseHits = new RaycastHit[128];
    private readonly Dictionary<Collider, Surface> _surfaces = new();
    private readonly int _mask;
    private AudioListener? _listener;
    private float _nextProbe, _nextListener;
    private int _blocked;
    internal readonly ObstructionFade Fade = new();
    internal int BlockedPaths => _blocked;
    internal float DistanceGain { get; private set; }
    internal float Gain => DistanceGain * Fade.Gain;

    private readonly struct Surface
    {
        internal readonly bool Blocks;
        internal readonly Vector3 Scale;
        internal Surface(bool blocks, Vector3 scale) { Blocks = blocks; Scale = scale; }
    }

    internal RadioObstruction(Transform radio)
    {
        _radio = radio;
        // Probe from the visible radio's centre, not its floor/wall placement
        // pivot, which may sit on the supporting wall or counter surface.
        var renderers = radio.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            _emitterOffset = radio.InverseTransformPoint(bounds.center);
        }
        // Resolve layer names rather than pinning numeric IDs. New physical
        // layers remain eligible; known interaction/placement helpers do not.
        _mask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Player", "UI", "Raycast Only", "PlaceableSurface", "PlacementCollision",
            "TempPlacementCollision", "Creation", "Inventory", "ShelfTargeting", "LightZone", "DecalDirt", "PlayerEntersTrigger", "IconGenerator", "CookingSlot", "PostProcessing");
    }

    internal void Reset() { Fade.Reset(); _blocked = 0; _nextProbe = 0; }

    internal void Update(bool nearby, int volume, bool audible)
    {
        if (!nearby) Reset();
        float now = Time.realtimeSinceStartup;
        if (!_listener || !_listener.isActiveAndEnabled)
        {
            if (now < _nextListener) { DistanceGain = 0; return; }
            _nextListener = now + 1;
            foreach (var listener in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (listener.isActiveAndEnabled) { _listener = listener; _nextProbe = 0; break; }
            if (!_listener || !_listener.isActiveAndEnabled) { DistanceGain = 0; return; }
        }
        Vector3 origin = _radio.TransformPoint(_emitterOffset), ear = _listener.transform.position;
        DistanceGain = RadioRange.Gain(nearby, volume, (ear - origin).magnitude);
        if (!nearby || !audible || DistanceGain <= 0) { _nextProbe = 0; return; }
        if (now >= _nextProbe)
        {
            _nextProbe = now + AcousticRules.ProbeInterval;
            // A broader aperture softens doorway/corner edges. Derive it from
            // the sound path, so merely turning the camera cannot change it.
            Vector3 side = Vector3.Cross(Vector3.up, ear - origin).normalized * .35f;
            _blocked = (Blocked(origin, ear) ? 1 : 0)
                + (Blocked(origin + Vector3.up * .06f, ear + side) ? 1 : 0)
                + (Blocked(origin - Vector3.up * .06f, ear - side) ? 1 : 0)
                + (Blocked(origin, ear + side * 2) ? 1 : 0)
                + (Blocked(origin, ear - side * 2) ? 1 : 0);
        }
        Fade.Advance(_blocked, AcousticRules.ProbePaths, Time.unscaledDeltaTime);
    }

    private bool Blocked(Vector3 start, Vector3 end)
    {
        Vector3 delta = end - start; float distance = delta.magnitude;
        if (distance < .05f) return false;
        var direction = delta / distance;
        // Mesh faces can point only one way. Probe both directions instead of
        // changing Physics.queriesHitBackfaces for the rest of the game.
        return Cast(start, direction, distance) || Cast(end, -direction, distance);
    }

    private bool Cast(Vector3 origin, Vector3 direction, float distance)
    {
        var hits = _hits;
        int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, _mask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length)
        { hits = _denseHits; count = Physics.RaycastNonAlloc(origin, direction, hits, distance, _mask, QueryTriggerInteraction.Ignore); }
        for (int i = 0; i < count; i++)
        {
            var collider = hits[i].collider;
            if (!collider || collider is CapsuleCollider or SphereCollider or CharacterController
                || hits[i].distance >= distance - .01f || collider.transform.IsChildOf(_radio)
                || _listener && (collider.transform.IsChildOf(_listener.transform) || _listener.transform.IsChildOf(collider.transform))) continue;
            Vector3 scale = collider.transform.lossyScale;
            if (!_surfaces.TryGetValue(collider, out var surface) || surface.Scale != scale)
            {
                var owner = collider.GetComponentInParent<RuntimeInteractable>();
                string path = collider.name;
                var parent = collider.transform.parent;
                for (int n = 0; parent && n < 8; n++, parent = parent.parent) path = parent.name + "/" + path;
                var size = collider.bounds.size;
                bool blocks = AcousticRules.Blocks(owner ? owner.name : "", path, LayerMask.LayerToName(collider.gameObject.layer),
                    owner, collider.attachedRigidbody, size.x, size.y, size.z);
                if (_surfaces.Count >= 512) _surfaces.Clear();
                // Newly placed decorations animate from a tiny scale. A cached
                // early size must not make their final wall permanently porous.
                surface = new Surface(blocks, scale); _surfaces[collider] = surface;
            }
            if (surface.Blocks) return true;
        }
        // A saturated fallback buffer cannot prove a clear route.
        return count == hits.Length;
    }
}
