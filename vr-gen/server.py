"""
VR Environment Generator -- Laptop Server (Shap-E local)
---------------------------------------------------------
Endpoints:
  POST /transcribe   -- receives WAV audio, returns text via local Whisper
  POST /generate     -- generates 3D model via local Shap-E, returns GLB
  GET  /health       -- liveness check

Install deps:
  pip install fastapi uvicorn openai-whisper python-multipart trimesh numpy torch --break-system-packages
  pip install git+https://github.com/openai/shap-e.git --break-system-packages

Run:
  python server.py

Notes:
  - Shap-E models (~1 GB) are downloaded automatically on first run and cached.
  - CPU (Mac): ~5-10 min per model. GPU: ~30-60 sec.
  - No API key required.
"""

import os
import tempfile
import logging
import io
import numpy as np
from pathlib import Path

import whisper
import trimesh
import torch

from shap_e.diffusion.sample import sample_latents
from shap_e.diffusion.gaussian_diffusion import diffusion_from_config
from shap_e.models.download import load_model, load_config

# shap_e.util.notebooks imports ipywidgets/IPython at module level which
# aren't installed. Mock them out so we can import decode_latent_mesh safely.
import sys
from unittest.mock import MagicMock
for _mod in ("ipywidgets", "IPython", "IPython.display"):
    if _mod not in sys.modules:
        sys.modules[_mod] = MagicMock()

from shap_e.util.notebooks import decode_latent_mesh

from fastapi import FastAPI, File, UploadFile, Form, HTTPException
from fastapi.responses import Response
import uvicorn

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger(__name__)

# -------------------------------------------------------
# Device selection
# -------------------------------------------------------
DEVICE = torch.device("cuda" if torch.cuda.is_available() else "cpu")
log.info("Using device: " + str(DEVICE))

# -------------------------------------------------------
# Load Whisper
# -------------------------------------------------------
log.info("Loading Whisper model...")
whisper_model = whisper.load_model("base")
log.info("Whisper ready.")

# -------------------------------------------------------
# Load Shap-E  (~1 GB download on first run, then cached)
# -------------------------------------------------------
log.info("Loading Shap-E models (may download ~1 GB on first run)...")
xm           = load_model("transmitter", device=DEVICE)
shap_e_model = load_model("text300M",    device=DEVICE)
diffusion    = diffusion_from_config(load_config("diffusion"))
log.info("Shap-E ready.")

# -------------------------------------------------------
# FastAPI app
# -------------------------------------------------------
app = FastAPI(title="VR-LVM Server (Shap-E local)")


@app.get("/health")
def health():
    return {
        "status":  "ok",
        "backend": "shap-e-local",
        "device":  str(DEVICE),
    }


@app.post("/transcribe")
async def transcribe(audio: UploadFile = File(...)):
    import traceback
    data   = await audio.read()
    log.info(f"Received audio: {len(data)} bytes, filename: {audio.filename}")
    suffix = Path(audio.filename or "audio.wav").suffix or ".wav"
    with tempfile.NamedTemporaryFile(suffix=suffix, delete=False) as tmp:
        tmp.write(data)
        tmp_path = tmp.name
    try:
        result = whisper_model.transcribe(tmp_path, fp16=False)
        text   = result["text"].strip()
        log.info("Transcribed: " + repr(text))
        return {"text": text}
    except Exception as e:
        log.error("Transcription failed: " + str(e))
        log.error(traceback.format_exc())
        raise HTTPException(500, "Transcription failed: " + str(e))
    finally:
        os.unlink(tmp_path)


@app.post("/generate")
async def generate(
    prompt:          str   = Form(...),
    guidance_scale:  float = Form(15.0),
    diffusion_steps: int   = Form(64),
):
    """
    Generates a 3D mesh locally using Shap-E.
    Returns a GLB binary (model/gltf-binary).

    Typical times:
      CPU (Apple Silicon / Intel Mac): 5-10 minutes
      CUDA GPU:                        30-60 seconds
    """
    log.info("Generating via Shap-E: " + repr(prompt))
    log.info("guidance_scale=" + str(guidance_scale) + "  steps=" + str(diffusion_steps))

    # ----------------------------------------------------------
    # 1. Sample latents
    # ----------------------------------------------------------
    try:
        use_fp16 = (DEVICE.type == "cuda")

        latents = sample_latents(
            batch_size=1,
            model=shap_e_model,
            diffusion=diffusion,
            guidance_scale=guidance_scale,
            model_kwargs={"texts": [prompt]},
            progress=True,
            clip_denoised=True,
            use_fp16=use_fp16,
            use_karras=True,
            karras_steps=diffusion_steps,
            sigma_min=1e-3,
            sigma_max=160,
            s_churn=0,
        )
    except Exception as e:
        log.error("Shap-E sampling error: " + str(e))
        raise HTTPException(500, "Shap-E generation failed: " + str(e))

    # ----------------------------------------------------------
    # 2. Decode latent -> triangle mesh -> GLB
    # ----------------------------------------------------------
    try:
        t = decode_latent_mesh(xm, latents[0]).tri_mesh()

        # Build per-vertex RGBA colors if the model produced them
        vertex_colors = None
        if all(k in t.vertex_channels for k in ("R", "G", "B")):
            r = np.clip(t.vertex_channels["R"], 0.0, 1.0)
            g = np.clip(t.vertex_channels["G"], 0.0, 1.0)
            b = np.clip(t.vertex_channels["B"], 0.0, 1.0)
            a = np.ones_like(r)
            vertex_colors = (np.stack([r, g, b, a], axis=-1) * 255).astype(np.uint8)

        mesh = trimesh.Trimesh(
            vertices=t.verts,
            faces=t.faces,
            vertex_colors=vertex_colors,
            process=False,
        )

        export_result = mesh.export(file_type="glb")
        # trimesh returns bytes; guard against any edge-case types
        if isinstance(export_result, (bytes, bytearray)):
            glb_bytes = bytes(export_result)
        else:
            buf = io.BytesIO()
            buf.write(export_result)
            glb_bytes = buf.getvalue()

        log.info("GLB size: " + str(round(len(glb_bytes) / 1024, 1)) + " KB")

    except Exception as e:
        log.error("Mesh export error: " + str(e))
        raise HTTPException(500, "Mesh export failed: " + str(e))

    # ----------------------------------------------------------
    # 3. Sanity check magic bytes
    # ----------------------------------------------------------
    if glb_bytes[:4] != b"glTF":
        raise HTTPException(
            500,
            "Output is not a valid GLB (got: " + str(glb_bytes[:20]) + ")"
        )

    log.info("GLB valid -- returning to Unity")
    return Response(content=glb_bytes, media_type="model/gltf-binary")


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8765, log_level="info")
