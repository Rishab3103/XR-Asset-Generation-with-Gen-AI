/*
 * VRGenTest.cs
 * ------------
 * Quest-only generation test. Attach to any GameObject in the VR scene.
 * Do NOT use SimpleGenTest.cs alongside this -- pick one or the other.
 *
 * Controls:
 *   Press Right Trigger  ->  sends prompt to server, generates 3D model
 *   Model spawns 2m in front of wherever you are looking
 *
 * Setup:
 *   1. Set Server Url  ->  http://YOUR_MAC_IP:8765  (NOT localhost)
 *   2. Set Prompt      ->  e.g. "a wooden chair"
 *   3. Build and Run to Quest (File > Build Settings > Android > Build and Run)
 */

using System.Collections;
using GLTFast;
using UnityEngine;
using UnityEngine.Networking;

public class VRGenTest : MonoBehaviour
{
    [Header("Server")]
    [Tooltip("Your Mac's LAN IP -- NOT localhost. e.g. http://192.168.2.1:8765")]
    public string serverUrl = "http://192.168.2.1:8765";

    [Header("Generation")]
    public string prompt = "a wooden chair";

    // ── state ────────────────────────────────────────────────────────────────
    bool _busy = false;

    void Update()
    {
        // OVRInput.GetDown fires exactly once on the frame the trigger is pressed
        if (
            !_busy
            && OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch)
        )
            StartCoroutine(Generate());
    }

    IEnumerator Generate()
    {
        _busy = true;
        Debug.Log("[VRGen] Sending prompt: " + prompt);

        // ── POST /generate ───────────────────────────────────────────────────
        WWWForm form = new WWWForm();
        form.AddField("prompt", prompt);

        string url = serverUrl.TrimEnd('/') + "/generate";
        using UnityWebRequest req = UnityWebRequest.Post(url, form);
        req.timeout = 1200;
        req.downloadHandler = new DownloadHandlerBuffer();

        Debug.Log("[VRGen] Waiting for server (5-10 min on CPU)...");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[VRGen] Request failed: " + req.error);
            _busy = false;
            yield break;
        }

        byte[] glbBytes = req.downloadHandler.data;
        Debug.Log("[VRGen] Received " + (glbBytes.Length / 1024) + " KB");

        if (
            glbBytes.Length < 4
            || glbBytes[0] != 0x67
            || glbBytes[1] != 0x6C
            || glbBytes[2] != 0x54
            || glbBytes[3] != 0x46
        )
        {
            Debug.LogError("[VRGen] Not a valid GLB.");
            _busy = false;
            yield break;
        }

        // ── Load GLB ─────────────────────────────────────────────────────────
        Debug.Log("[VRGen] Loading GLB...");
        var gltf = new GltfImport();
        var loadTask = gltf.LoadGltfBinary(glbBytes);
        yield return new WaitUntil(() => loadTask.IsCompleted);

        if (!loadTask.Result)
        {
            Debug.LogError("[VRGen] GLTFast failed to parse the model.");
            _busy = false;
            yield break;
        }

        // ── Spawn 2m in front of the camera, always above ground ─────────────
        GameObject container = new GameObject("Gen_" + prompt);
        Camera cam = Camera.main;
        Vector3 spawnPos;
        if (cam != null)
        {
            // Use only the horizontal forward direction so the model
            // doesn't spawn above/below when looking up or down
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.Normalize();
            spawnPos = cam.transform.position + forward * 2f;
            spawnPos.y = 0f; // place on the floor
        }
        else
        {
            spawnPos = new Vector3(0f, 0f, 2f);
        }
        container.transform.position = spawnPos;
        Debug.Log("[VRGen] Spawning at: " + spawnPos);

        var instantiateTask = gltf.InstantiateMainSceneAsync(container.transform);
        yield return new WaitUntil(() => instantiateTask.IsCompleted);

        // ── Auto-scale to ~1 m ───────────────────────────────────────────────
        Renderer[] renderers = container.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largest > 0.001f)
                container.transform.localScale = Vector3.one * (1f / largest);
        }

        // ── Apply material with colour parsed from prompt ─────────────────────
        Shader shader =
            Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Diffuse");

        if (shader != null)
        {
            Color col = ColorFromPrompt(prompt);
            foreach (Renderer r in renderers)
            {
                Material[] mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = new Material(shader) { color = col };
                r.materials = mats;
            }
            Debug.Log("[VRGen] Applied colour: " + ColorUtility.ToHtmlStringRGB(col));
        }

        Debug.Log("[VRGen] Done! Model spawned.");
        _busy = false;
    }

    // ── Extracts a colour from keywords in the prompt ─────────────────────────
    static Color ColorFromPrompt(string prompt)
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
        return new Color(0.75f, 0.75f, 0.75f); // default light grey
    }
}
