/*
 * ServerClient.cs
 * ---------------
 * Thin HTTP wrapper around the laptop server's two endpoints.
 * Uses UnityWebRequest so it works on Quest without extra dependencies.
 *
 * No MonoBehaviour — instantiated and owned by VRGenManager.
 */

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace VRGen
{
    public class ServerClient
    {
        readonly string _base;

        public ServerClient(string baseUrl)
        {
            _base = baseUrl.TrimEnd('/');
        }

        // ── POST /transcribe ─────────────────────────────────────────────
        /// <summary>
        /// Sends WAV bytes, calls onResult with transcribed text (or null on error).
        /// </summary>
        public IEnumerator Transcribe(byte[] wavBytes, Action<string> onResult)
        {
            var form = new WWWForm();
            form.AddBinaryData("audio", wavBytes, "recording.wav", "audio/wav");

            using var req = UnityWebRequest.Post($"{_base}/transcribe", form);
            req.timeout = 30;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[VRGen] /transcribe error: {req.error}\n{req.downloadHandler.text}");
                onResult(null);
                yield break;
            }

            var json = req.downloadHandler.text;
            // Simple parse — avoid JSON library dependency
            string text = ExtractJsonString(json, "text");
            onResult(text);
        }

        // ── POST /generate ───────────────────────────────────────────────
        /// <summary>
        /// Sends prompt text, calls onResult with raw GLB bytes (or null on error).
        /// </summary>
        public IEnumerator Generate(string prompt, Action<byte[]> onResult,
                                    float guidanceScale = 15f, int steps = 64)
        {
            var form = new WWWForm();
            form.AddField("prompt",          prompt);
            form.AddField("guidance_scale",  guidanceScale.ToString("F1",
                              System.Globalization.CultureInfo.InvariantCulture));
            form.AddField("steps",           steps.ToString());

            using var req = UnityWebRequest.Post($"{_base}/generate", form);
            req.timeout = 1200;  // Shap-E on CPU can take 10+ min
            req.downloadHandler = new DownloadHandlerBuffer();
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[VRGen] /generate error: {req.error}\n{req.downloadHandler.text}");
                onResult(null);
                yield break;
            }

            onResult(req.downloadHandler.data);
        }

        // ── Tiny JSON string extractor (no dependency) ───────────────────
        static string ExtractJsonString(string json, string key)
        {
            string search = $"\"{key}\"";
            int ki = json.IndexOf(search, StringComparison.Ordinal);
            if (ki < 0) return null;
            int colon = json.IndexOf(':', ki + search.Length);
            if (colon < 0) return null;
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }
    }
}
