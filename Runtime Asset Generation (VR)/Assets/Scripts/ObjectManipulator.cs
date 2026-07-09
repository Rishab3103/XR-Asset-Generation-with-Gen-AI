/*
 * ObjectManipulator.cs
 * --------------------
 * Attach automatically to every placed object (done by VRARManager.ConfirmPlacement).
 * Driven externally by ManipulationManager — no direct input here.
 *
 * States:
 *   Default  — sits in world, can be grabbed
 *   Grabbed  — follows hand anchor (green tint)
 *   Locked   — frozen in place, won't move until unlocked (blue tint)
 */

using UnityEngine;

public class ObjectManipulator : MonoBehaviour
{
    public bool IsGrabbed => _anchor != null;
    public bool IsLocked  => _locked;

    // ── Private ───────────────────────────────────────────────────────────────
    Transform  _anchor;
    Vector3    _localPos;
    Quaternion _localRot;
    Renderer[] _rends;
    Color[]    _origColors;
    bool       _locked;

    // ── Init ──────────────────────────────────────────────────────────────────

    void Awake()
    {
        EnsureCollider();
        CacheColors();
    }

    /// Add a BoxCollider sized to all child renderers if none already exists.
    void EnsureCollider()
    {
        if (GetComponentInChildren<Collider>() != null) return;

        var rends = GetComponentsInChildren<Renderer>();
        var box   = gameObject.AddComponent<BoxCollider>();
        if (rends.Length == 0) return;

        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);

        // Convert world-space bounds into local space
        box.center = transform.InverseTransformPoint(b.center);
        box.size   = Vector3.Scale(b.size,
                         new Vector3(1f / transform.lossyScale.x,
                                     1f / transform.lossyScale.y,
                                     1f / transform.lossyScale.z));
    }

    void CacheColors()
    {
        _rends      = GetComponentsInChildren<Renderer>();
        _origColors = new Color[_rends.Length];
        for (int i = 0; i < _rends.Length; i++)
            if (_rends[i].material != null)
                _origColors[i] = _rends[i].material.color;
    }

    // ── Per-frame update ──────────────────────────────────────────────────────

    void Update()
    {
        if (_anchor == null) return;
        transform.position = _anchor.TransformPoint(_localPos);
        transform.rotation = _anchor.rotation * _localRot;
    }

    // ── Public API (called by ManipulationManager) ────────────────────────────

    public void Grab(Transform anchor)
    {
        if (_locked) return;
        _anchor   = anchor;
        _localPos = anchor.InverseTransformPoint(transform.position);
        _localRot = Quaternion.Inverse(anchor.rotation) * transform.rotation;
        ApplyTint(new Color(0.55f, 1f, 0.55f));   // green = grabbed
    }

    public void Release()
    {
        _anchor = null;
        if (!_locked) ClearTint();
    }

    /// Lock = save in place. Object won't respond to grab until unlocked.
    public void Lock()
    {
        if (IsGrabbed) Release();
        _locked = true;
        ApplyTint(new Color(0.55f, 0.75f, 1f));   // blue = locked
    }

    public void Unlock()
    {
        _locked = false;
        ClearTint();
    }

    /// Scale uniformly by a multiplicative factor (called each frame during two-hand scale).
    public void ScaleBy(float factor)
    {
        if (_locked) return;
        Vector3 s = transform.localScale * factor;
        s = Vector3.Max(s, Vector3.one * 0.02f);
        s = Vector3.Min(s, Vector3.one * 10f);
        transform.localScale = s;
    }

    // ── Tint helpers ──────────────────────────────────────────────────────────

    void ApplyTint(Color c)
    {
        foreach (var r in _rends)
            if (r.material != null) r.material.color = c;
    }

    void ClearTint()
    {
        for (int i = 0; i < _rends.Length; i++)
            if (_rends[i] != null && _rends[i].material != null)
                _rends[i].material.color = _origColors[i];
    }
}
