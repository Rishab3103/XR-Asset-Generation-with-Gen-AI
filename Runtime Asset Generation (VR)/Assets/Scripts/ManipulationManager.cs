/*
 * ManipulationManager.cs
 * ----------------------
 * Handles all manipulation input for placed objects (ObjectManipulator components).
 * Add this to the same GameObject as VRARManager (or any active scene object).
 * Assign the same Right/LeftHandAnchor transforms used by VRARManager.
 *
 * Controls:
 *   Right Grip (hold)                  → grab aimed object (translate + rotate with hand)
 *   Left Grip (hold) while right grabs → two-hand pinch scale
 *   A button                           → lock grabbed object in place (save position)
 *   B button                           → delete grabbed object
 *
 * Note: A button lock only fires when the inventory panel is closed,
 *       to avoid conflict with inventory selection in VRARManager.
 */

using UnityEngine;

[DefaultExecutionOrder(10)]   // run after VRARManager
public class ManipulationManager : MonoBehaviour
{
    public static ManipulationManager Instance { get; private set; }

    [Header("XR Anchors")]
    public Transform rightHandAnchor;
    public Transform leftHandAnchor;

    [Header("Grab Ray")]
    [Tooltip("Max distance the grab ray can reach")]
    public float grabRayLength = 5f;
    public LayerMask grabMask  = ~0;

    // ── Runtime state ─────────────────────────────────────────────────────────
    ObjectManipulator _grabbed;
    bool  _scaling;
    float _prevHandDist;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake() => Instance = this;

    void Update()
    {
        // ── Grab / release ────────────────────────────────────────────────────
        if (OVRInput.GetDown(OVRInput.RawButton.RHandTrigger)) TryGrab();
        if (OVRInput.GetUp(OVRInput.RawButton.RHandTrigger))   ReleaseGrab();

        // ── Two-hand scale ────────────────────────────────────────────────────
        if (OVRInput.GetDown(OVRInput.RawButton.LHandTrigger) && _grabbed != null)
            BeginScale();
        if (OVRInput.GetUp(OVRInput.RawButton.LHandTrigger))
            _scaling = false;
        if (_scaling && _grabbed != null)
            DoScale();

        // ── A button → lock grabbed object in place ───────────────────────────
        if (OVRInput.GetDown(OVRInput.RawButton.A)
            && !VRARManager.InventoryOpen
            && _grabbed != null)
        {
            _grabbed.Lock();
            _grabbed = null;
            _scaling = false;
        }

        // ── B button → delete grabbed object ──────────────────────────────────
        if (OVRInput.GetDown(OVRInput.RawButton.B) && _grabbed != null)
        {
            Destroy(_grabbed.gameObject);
            _grabbed = null;
            _scaling = false;
        }
    }

    // ── Grab ──────────────────────────────────────────────────────────────────

    void TryGrab()
    {
        if (rightHandAnchor == null) return;

        var ray = new Ray(rightHandAnchor.position, rightHandAnchor.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, grabRayLength, grabMask)) return;

        var m = hit.collider.GetComponentInParent<ObjectManipulator>();
        if (m == null || m.IsLocked || m.IsGrabbed) return;

        _grabbed = m;
        _grabbed.Grab(rightHandAnchor);
        Debug.Log("[Manip] Grabbed: " + _grabbed.name);
    }

    void ReleaseGrab()
    {
        if (_grabbed != null)
        {
            _grabbed.Release();
            Debug.Log("[Manip] Released: " + _grabbed.name);
        }
        _grabbed = null;
        _scaling = false;
    }

    // ── Scale ─────────────────────────────────────────────────────────────────

    void BeginScale()
    {
        if (leftHandAnchor == null) return;
        _scaling      = true;
        _prevHandDist = HandDistance();
        Debug.Log("[Manip] Scale started, initial dist: " + _prevHandDist.ToString("F3"));
    }

    void DoScale()
    {
        float dist   = HandDistance();
        float factor = dist / Mathf.Max(_prevHandDist, 0.001f);
        _grabbed.ScaleBy(factor);
        _prevHandDist = dist;
    }

    float HandDistance() =>
        leftHandAnchor != null
            ? Vector3.Distance(rightHandAnchor.position, leftHandAnchor.position)
            : 0f;

    // ── Public helpers ────────────────────────────────────────────────────────

    /// Returns the currently grabbed object, or null.
    public ObjectManipulator CurrentGrab => _grabbed;
}
