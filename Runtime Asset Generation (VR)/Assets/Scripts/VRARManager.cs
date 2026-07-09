/*
 * VRARManager.cs  —  AR Asset Generation
 * ----------------------------------------
 * Controls:
 *   Hold Right Trigger        → record voice
 *   Release                   → transcribe (Whisper) + generate (Shap-E)
 *   Right Trigger (Placing)   → place / confirm object
 *   Left Menu                 → toggle inventory
 *   (Inventory open)
 *     Right Thumbstick up/dn  → navigate items
 *     Right Trigger           → re-spawn selected item (no re-generation)
 *
 * Manipulation (handled by ManipulationManager):
 *   Right Grip                → grab, translate, rotate
 *   Right + Left Grip         → two-hand scale
 *   A button                  → lock / save object in place
 *   B button                  → delete object
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GLTFast;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class VRARManager : MonoBehaviour
{
    // ── Static ─────────────────────────────────────────────────────────────────
    /// Read by ManipulationManager to suppress A-button lock when inventory is open.
    public static bool InventoryOpen { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Server")]
    public string serverUrl = "http://192.168.2.1:8765";

    [Header("XR")]
    public Transform rightHandAnchor;
    public Transform leftHandAnchor;

    [Header("UI")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI transcriptText;
    public GameObject inventoryPanel;
    public Transform inventoryContent;
    public GameObject inventoryItemPrefab;

    [Header("Placement")]
    public LayerMask placementMask = ~0;
    public float placementRayLength = 8f;

    [Header("Audio")]
    public int maxRecordSeconds = 15;

    // ── State ─────────────────────────────────────────────────────────────────
    enum State
    {
        Idle,
        Recording,
        Transcribing,
        Generating,
        Placing,
    }

    State _state = State.Idle;

    AudioClip _clip;
    GameObject _ghost;
    string _currentPrompt;
    byte[] _pendingGlb; // GLB bytes from most recent generation
    bool _prevTrigger;

    // ── Inventory ─────────────────────────────────────────────────────────────
    [Serializable]
    class InventoryEntry
    {
        public string prompt;
        public byte[] glbBytes; // kept in-memory for instant re-spawn
        public GameObject instance; // the live placed object (may be null if deleted)
    }

    readonly List<InventoryEntry> _inventory = new();
    bool _inventoryOpen;
    int _selectedIndex;
    float _stickCooldown;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    IEnumerator Start()
    {
        if (inventoryPanel)
            inventoryPanel.SetActive(false);

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            SetStatus("Requesting microphone permission...");
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        }

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            SetStatus("ERROR: Microphone permission denied.");
            yield break;
        }

        SetStatus("Hold [Right Trigger] to speak and generate");
    }

    void Update()
    {
        bool trigger = OVRInput.Get(
            OVRInput.Button.PrimaryIndexTrigger,
            OVRInput.Controller.RTouch
        );
        bool triggerDn = trigger && !_prevTrigger;
        _prevTrigger = trigger;

        // ── Right trigger ──────────────────────────────────────────────────────
        if (triggerDn)
        {
            if (_inventoryOpen && _inventory.Count > 0)
                StartCoroutine(RespawnFromInventory(_inventory[_selectedIndex]));
            else if (_state == State.Idle)
                BeginRecording();
            else if (_state == State.Placing)
                ConfirmPlacement();
        }

        if (!trigger && _state == State.Recording)
            StopAndProcess();

        // ── Ghost ray ─────────────────────────────────────────────────────────
        if (_state == State.Placing && _ghost != null)
            UpdateGhostRay();

        // ── Inventory toggle (Left Menu) ──────────────────────────────────────
        if (OVRInput.GetDown(OVRInput.Button.Start))
            ToggleInventory();

        // ── Inventory navigation (right thumbstick Y) ─────────────────────────
        if (_inventoryOpen && _inventory.Count > 0)
        {
            _stickCooldown -= Time.deltaTime;
            float y = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).y;
            if (_stickCooldown <= 0f && Mathf.Abs(y) > 0.5f)
            {
                _selectedIndex =
                    (_selectedIndex + (y > 0 ? -1 : 1) + _inventory.Count) % _inventory.Count;
                _stickCooldown = 0.3f;
                RefreshInventoryUI();
            }
        }
    }

    // ── Recording ─────────────────────────────────────────────────────────────

    void BeginRecording()
    {
        _state = State.Recording;
        _clip = Microphone.Start(null, false, maxRecordSeconds, 16000);
        SetStatus("Recording... release trigger to send");
    }

    void StopAndProcess()
    {
        _state = State.Transcribing;
        int samples = Microphone.GetPosition(null);
        Microphone.End(null);

        if (samples < 1600)
        {
            SetStatus("Too short. Hold trigger and speak.");
            _state = State.Idle;
            return;
        }

        StartCoroutine(TranscribeAndGenerate(ClipToWav(_clip, samples)));
    }

    // ── Generation pipeline ───────────────────────────────────────────────────

    IEnumerator TranscribeAndGenerate(byte[] wav)
    {
        // 1. Transcribe
        SetStatus("Transcribing...");
        string prompt = null;
        yield return StartCoroutine(PostTranscribe(wav, t => prompt = t));

        if (string.IsNullOrWhiteSpace(prompt))
        {
            SetStatus("Couldn't transcribe. Try again.");
            _state = State.Idle;
            yield break;
        }

        if (transcriptText)
            transcriptText.text = "\"" + prompt + "\"";
        _currentPrompt = prompt;

        // 2. Generate
        SetStatus("Generating: " + prompt + "\n(5-10 min on CPU)");
        _state = State.Generating;

        byte[] glb = null;
        yield return StartCoroutine(PostGenerate(prompt, b => glb = b));

        if (glb == null)
        {
            SetStatus("Generation failed. Check server logs.");
            _state = State.Idle;
            yield break;
        }

        _pendingGlb = glb; // cache for ConfirmPlacement → InventoryEntry

        // 3. Load ghost for placement
        SetStatus("Loading model...");
        yield return StartCoroutine(LoadGhost(prompt, glb));
    }

    IEnumerator RespawnFromInventory(InventoryEntry entry)
    {
        if (entry.glbBytes == null)
        {
            SetStatus("No cached data for this item.");
            yield break;
        }

        ToggleInventory(); // close inventory panel
        _currentPrompt = entry.prompt;
        _pendingGlb = entry.glbBytes;

        if (transcriptText)
            transcriptText.text = "(re-spawn) \"" + entry.prompt + "\"";
        SetStatus("Loading: " + entry.prompt);
        yield return StartCoroutine(LoadGhost(entry.prompt, entry.glbBytes));
    }

    // ── Server calls ──────────────────────────────────────────────────────────

    IEnumerator PostTranscribe(byte[] wav, Action<string> onResult)
    {
        var form = new WWWForm();
        form.AddBinaryData("audio", wav, "recording.wav", "audio/wav");

        using var req = UnityWebRequest.Post(serverUrl.TrimEnd('/') + "/transcribe", form);
        req.timeout = 30;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[AR] Transcribe: " + req.error);
            onResult(null);
            yield break;
        }

        string json = req.downloadHandler.text;
        int ki = json.IndexOf("\"text\"", StringComparison.Ordinal);
        if (ki < 0)
        {
            onResult(null);
            yield break;
        }
        int q1 = json.IndexOf('"', ki + 6);
        int q2 = json.IndexOf('"', q1 + 1);
        onResult(q1 >= 0 && q2 > q1 ? json.Substring(q1 + 1, q2 - q1 - 1) : null);
    }

    IEnumerator PostGenerate(string prompt, Action<byte[]> onResult)
    {
        var form = new WWWForm();
        form.AddField("prompt", prompt);

        using var req = UnityWebRequest.Post(serverUrl.TrimEnd('/') + "/generate", form);
        req.timeout = 1200;
        req.downloadHandler = new DownloadHandlerBuffer();
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[AR] Generate: " + req.error);
            onResult(null);
            yield break;
        }

        onResult(req.downloadHandler.data);
    }

    // ── Ghost & placement ─────────────────────────────────────────────────────

    IEnumerator LoadGhost(string prompt, byte[] glb)
    {
        if (_ghost != null)
            Destroy(_ghost);

        var gltf = new GltfImport();
        var task = gltf.LoadGltfBinary(glb);
        yield return new WaitUntil(() => task.IsCompleted);

        if (!task.Result)
        {
            SetStatus("Model parse failed.");
            _state = State.Idle;
            yield break;
        }

        _ghost = new GameObject("Ghost_" + prompt);
        var inst = gltf.InstantiateMainSceneAsync(_ghost.transform);
        yield return new WaitUntil(() => inst.IsCompleted);

        AutoScale(_ghost, 0.3f);
        ApplyMaterial(_ghost, prompt, ghost: true);

        _state = State.Placing;
        SetStatus("Aim and pull [Right Trigger] to place");
    }

    void UpdateGhostRay()
    {
        if (rightHandAnchor == null)
            return;
        Ray ray = new Ray(rightHandAnchor.position, rightHandAnchor.forward);
        _ghost.transform.position = Physics.Raycast(
            ray,
            out RaycastHit hit,
            placementRayLength,
            placementMask
        )
            ? hit.point
            : rightHandAnchor.position + rightHandAnchor.forward * 1f;
        _ghost.SetActive(true);
    }

    void ConfirmPlacement()
    {
        if (_ghost == null)
            return;

        // Finalise material (remove ghost tint)
        ApplyMaterial(_ghost, _currentPrompt, ghost: false);
        _ghost.name = "Placed_" + _currentPrompt;

        // Add manipulator so the object can be grabbed, scaled, locked
        _ghost.AddComponent<ObjectManipulator>();

        // Save to inventory (keeps GLB bytes for re-spawn)
        _inventory.Add(
            new InventoryEntry
            {
                prompt = _currentPrompt,
                glbBytes = _pendingGlb,
                instance = _ghost,
            }
        );
        RefreshInventoryUI();

        _ghost = null;
        _pendingGlb = null;
        _state = State.Idle;
        SetStatus("Placed!\n[Right Grip] grab  •  [A] lock  •  [B] delete");
    }

    // ── Inventory UI ──────────────────────────────────────────────────────────

    void ToggleInventory()
    {
        _inventoryOpen = !_inventoryOpen;
        InventoryOpen = _inventoryOpen;
        if (inventoryPanel)
            inventoryPanel.SetActive(_inventoryOpen);
        if (_inventoryOpen)
            RefreshInventoryUI();
    }

    void RefreshInventoryUI()
    {
        if (inventoryContent == null || inventoryItemPrefab == null)
            return;

        // Clear existing buttons
        foreach (Transform child in inventoryContent)
            Destroy(child.gameObject);

        for (int i = 0; i < _inventory.Count; i++)
        {
            var go = Instantiate(inventoryItemPrefab, inventoryContent);
            var lbl = go.GetComponentInChildren<TextMeshProUGUI>();
            var img = go.GetComponent<Image>();

            if (lbl)
                lbl.text = _inventory[i].prompt;

            // Highlight the currently selected item
            if (img)
                img.color =
                    (i == _selectedIndex)
                        ? new Color(0.4f, 0.8f, 1f, 0.9f) // blue = selected
                        : new Color(1f, 1f, 1f, 0.4f); // faint = unselected
        }
    }

    // ── Material ──────────────────────────────────────────────────────────────

    static void ApplyMaterial(GameObject root, string prompt, bool ghost)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null)
            return;

        Color col = ColorFromPrompt(prompt);
        if (ghost)
            col = Color.Lerp(col, new Color(0.4f, 0.8f, 1f), 0.5f);

        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
                mats[i] = new Material(shader) { color = col };
            r.materials = mats;
        }
    }

    public static Color ColorFromPrompt(string prompt)
    {
        string p = prompt.ToLower();
        if (p.Contains("red"))
            return new Color(0.85f, 0.10f, 0.10f);
        if (p.Contains("blue"))
            return new Color(0.10f, 0.30f, 0.85f);
        if (p.Contains("green"))
            return new Color(0.10f, 0.65f, 0.20f);
        if (p.Contains("yellow"))
            return new Color(0.95f, 0.85f, 0.10f);
        if (p.Contains("orange"))
            return new Color(0.95f, 0.50f, 0.05f);
        if (p.Contains("purple") || p.Contains("violet"))
            return new Color(0.55f, 0.10f, 0.75f);
        if (p.Contains("pink"))
            return new Color(0.95f, 0.45f, 0.65f);
        if (p.Contains("cyan") || p.Contains("teal"))
            return new Color(0.10f, 0.75f, 0.80f);
        if (p.Contains("white"))
            return new Color(0.92f, 0.92f, 0.92f);
        if (p.Contains("black"))
            return new Color(0.08f, 0.08f, 0.08f);
        if (p.Contains("gray") || p.Contains("grey"))
            return new Color(0.50f, 0.50f, 0.50f);
        if (p.Contains("brown"))
            return new Color(0.45f, 0.25f, 0.10f);
        if (p.Contains("gold") || p.Contains("golden"))
            return new Color(0.95f, 0.80f, 0.10f);
        if (p.Contains("silver"))
            return new Color(0.75f, 0.75f, 0.78f);
        if (p.Contains("wood") || p.Contains("wooden"))
            return new Color(0.55f, 0.35f, 0.15f);
        if (
            p.Contains("metal")
            || p.Contains("steel")
            || p.Contains("iron")
            || p.Contains("chrome")
        )
            return new Color(0.68f, 0.68f, 0.72f);
        if (p.Contains("glass"))
            return new Color(0.70f, 0.88f, 0.95f);
        if (p.Contains("stone") || p.Contains("concrete") || p.Contains("rock"))
            return new Color(0.55f, 0.53f, 0.50f);
        return new Color(0.75f, 0.75f, 0.75f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static void AutoScale(GameObject root, float targetSize)
    {
        var rends = root.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0)
            return;
        Bounds b = rends[0].bounds;
        foreach (var r in rends)
            b.Encapsulate(r.bounds);
        float largest = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (largest > 0.001f)
            root.transform.localScale = Vector3.one * (targetSize / largest);
    }

    void SetStatus(string msg)
    {
        if (statusText)
            statusText.text = msg;
        Debug.Log("[AR] " + msg);
    }

    static byte[] ClipToWav(AudioClip clip, int sampleCount)
    {
        float[] samples = new float[sampleCount];
        clip.GetData(samples, 0);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        int ch = clip.channels,
            hz = clip.frequency,
            dataBytes = sampleCount * 2;

        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataBytes);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)ch);
        bw.Write(hz);
        bw.Write(hz * ch * 2);
        bw.Write((short)(ch * 2));
        bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataBytes);

        foreach (float s in samples)
            bw.Write((short)Mathf.Clamp(s * 32767f, -32768f, 32767f));

        return ms.ToArray();
    }
}
