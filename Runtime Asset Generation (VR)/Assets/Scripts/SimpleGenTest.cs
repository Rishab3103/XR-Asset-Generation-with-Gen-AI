/*
 * SimpleGenTest.cs
 * ----------------
 * Attach this to ANY GameObject in your scene.
 * Press SPACE -> sends prompt to server -> loads GLB -> spawns in front of camera.
 *
 * No other scripts required. No mic, no placement system, no UI needed.
 *
 * Setup:
 *   1. Set Server Url in Inspector (e.g. http://localhost:8765)
 *   2. Set Prompt in Inspector (e.g. "a wooden chair")
 *   3. Press Play, then press SPACE
 */

using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GLTFast;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class SimpleGenTest : MonoBehaviour
{
#if UNITY_EDITOR
    // Queued placements: prefab path + world transform, placed when Play mode ends
    struct Placement
    {
        public string path;
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 scale;
    }

    static readonly List<Placement> _pending = new();

    [InitializeOnLoadMethod]
    static void RegisterCallback()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || _pending.Count == 0)
            return;
        foreach (var p in _pending)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p.path);
            if (prefab == null)
                continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.SetPositionAndRotation(p.pos, p.rot);
            inst.transform.localScale = p.scale;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(
            "[SimpleGen] Placed " + _pending.Count + " model(s) in scene. Press Cmd+S to save."
        );
        _pending.Clear();
    }
#endif

    [Header("Server")]
    public string serverUrl = "http://localhost:8765";

    [Header("Generation")]
    public string prompt = "a wooden chair";

    // ── state ────────────────────────────────────────────────────────────────
    bool _busy = false;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && !_busy)
        {
            StartCoroutine(Generate());
        }
    }

    IEnumerator Generate()
    {
        _busy = true;
        Debug.Log("[SimpleGen] Sending prompt: " + prompt);

        // ── POST /generate ───────────────────────────────────────────────────
        WWWForm form = new WWWForm();
        form.AddField("prompt", prompt);

        string url = serverUrl.TrimEnd('/') + "/generate";
        using UnityWebRequest req = UnityWebRequest.Post(url, form);
        req.timeout = 900; // 15 min -- Shap-E on CPU is slow
        req.downloadHandler = new DownloadHandlerBuffer();

        Debug.Log("[SimpleGen] Waiting for server (this takes 5-10 min on CPU)...");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[SimpleGen] Request failed: " + req.error);
            Debug.LogError("[SimpleGen] Response: " + req.downloadHandler.text);
            _busy = false;
            yield break;
        }

        byte[] glbBytes = req.downloadHandler.data;
        Debug.Log("[SimpleGen] Received " + (glbBytes.Length / 1024) + " KB");

        if (
            glbBytes.Length < 4
            || glbBytes[0] != 0x67
            || glbBytes[1] != 0x6C
            || glbBytes[2] != 0x54
            || glbBytes[3] != 0x46
        )
        {
            Debug.LogError(
                "[SimpleGen] Response is not a GLB file. First bytes: "
                    + glbBytes[0]
                    + " "
                    + glbBytes[1]
                    + " "
                    + glbBytes[2]
                    + " "
                    + glbBytes[3]
            );
            _busy = false;
            yield break;
        }

        // ── Load GLB via GLTFast ─────────────────────────────────────────────
        Debug.Log("[SimpleGen] Loading GLB...");
        var gltf = new GltfImport();
        var loadTask = gltf.LoadGltfBinary(glbBytes);

        yield return new WaitUntil(() => loadTask.IsCompleted);

        if (!loadTask.Result)
        {
            Debug.LogError("[SimpleGen] GLTFast failed to load the model.");
            _busy = false;
            yield break;
        }

        // ── Instantiate 2m in front of the camera ────────────────────────────
        GameObject container = new GameObject("GeneratedModel_" + prompt);
        Camera cam = Camera.main;
        if (cam != null)
            container.transform.position = cam.transform.position + cam.transform.forward * 2f;

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

        // ── Apply a shader that reads vertex colours ──────────────────────────
        // Shap-E bakes colours as per-vertex RGB. Standard/Lit shaders ignore
        // vertex colours entirely -- Particles/Standard Unlit multiplies them in.
        Shader shader =
            Shader.Find("Particles/Standard Unlit") // Built-in RP
            ?? Shader.Find("Universal Render Pipeline/Particles/Unlit") // URP
            ?? Shader.Find("Standard"); // fallback

        if (shader != null)
        {
            foreach (Renderer r in renderers)
            {
                Material[] mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = new Material(shader) { color = Color.white };
                r.materials = mats;
            }
        }

        Debug.Log("[SimpleGen] Done! Model spawned: " + container.name);

#if UNITY_EDITOR
        // ── Save meshes, materials and prefab so the object survives Play mode ──
        string assetDir = "Assets/GeneratedModels";
        if (!AssetDatabase.IsValidFolder(assetDir))
            AssetDatabase.CreateFolder("Assets", "GeneratedModels");

        string safeName = Regex.Replace(prompt, @"[^a-zA-Z0-9]", "_");
        string timestamp = System.DateTime.Now.ToString("HHmmss");
        string uid = safeName + "_" + timestamp;

        // Save each mesh as a .asset so the prefab can reference it
        MeshFilter[] filters = container.GetComponentsInChildren<MeshFilter>();
        for (int mi = 0; mi < filters.Length; mi++)
        {
            if (filters[mi].sharedMesh == null)
                continue;
            Mesh saved = Object.Instantiate(filters[mi].sharedMesh);
            saved.name = uid + "_mesh" + mi;
            AssetDatabase.CreateAsset(saved, assetDir + "/" + saved.name + ".asset");
            filters[mi].sharedMesh = saved; // point MeshFilter at the saved asset
        }

        // Save each material as a .mat
        foreach (Renderer r in renderers)
        {
            Material[] mats = r.sharedMaterials;
            for (int mi = 0; mi < mats.Length; mi++)
            {
                if (mats[mi] == null)
                    continue;
                Material saved = new Material(mats[mi]);
                saved.name = uid + "_mat" + mi;
                AssetDatabase.CreateAsset(saved, assetDir + "/" + saved.name + ".mat");
                mats[mi] = saved;
            }
            r.sharedMaterials = mats;
        }

        AssetDatabase.SaveAssets();

        // Save as prefab
        string prefabPath = assetDir + "/" + uid + ".prefab";
        PrefabUtility.SaveAsPrefabAssetAndConnect(
            container,
            prefabPath,
            InteractionMode.AutomatedAction
        );

        AssetDatabase.Refresh();

        // Queue placement for when Play mode ends (MarkSceneDirty can't run in Play mode)
        _pending.Add(
            new Placement
            {
                path = prefabPath,
                pos = container.transform.position,
                rot = container.transform.rotation,
                scale = container.transform.localScale,
            }
        );

        Debug.Log(
            "[SimpleGen] Saved to "
                + prefabPath
                + " -- exit Play mode and it will appear in the scene automatically."
        );
#endif

        _busy = false;
    }
}
