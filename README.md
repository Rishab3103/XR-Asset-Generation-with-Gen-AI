# XR-Asset-Generation-with-Gen-AI

# Complete Setup & User Guide

> **Meta Quest 3 · Shap-E · Whisper · Unity 6000.2.10f1**
>
> Clone the repository and then implement the following steps.

---

## Table of Contents

- [Overview](#overview)
- [What You Need](#what-you-need)
- [Part A: Mac Server Setup](#part-a-mac-server-setup)
- [Part B: Unity Project Setup](#part-b-unity-project-setup)
- [Part C: Meta Quest 3 Setup](#part-c-meta-quest-3-setup)
- [Part D: Build and Deploy](#part-d-build-and-deploy)
- [Part E: Using the App](#part-e-using-the-app)
- [Part F: Controls Reference](#part-f-controls-reference)
- [Part G: Troubleshooting](#part-g-troubleshooting)
- [Part H: File Reference](#part-h-file-reference)
- [Quick Reference Card](#quick-reference-card)

---

## Overview

This project turns your Meta Quest 3 headset into an augmented reality workbench for generating 3D objects with your voice. Speak the name of any object — for example *"a red wooden chair"* — and an AI model called **Shap-E**, running locally on your Mac, generates a 3D mesh of it. The object then appears floating in your real room where you can grab, move, rotate, scale, and place it.

The system has two components:

- **The Mac Server** — a Python program running on your Mac that handles voice transcription (OpenAI Whisper) and 3D model generation (Shap-E). No internet connection or API keys needed after the initial one-time model download.
- **The Quest App** — a Meta Quest 3 application built in Unity that captures your voice, sends it to the Mac server, receives the generated 3D model, and provides full AR interaction controls.

> [!WARNING]
> 3D generation takes **5–10 minutes per object** on Mac CPU. This is completely normal — Shap-E is a large AI diffusion model. You can start generating your next object while waiting for the current one.

---

## What You Need

### Hardware

- A Mac computer (Apple Silicon M1/M2/M3 is significantly faster than Intel for generation)
- A Meta Quest 3 headset
- A USB-C cable that supports **data transfer** (not just charging)
- A Wi-Fi router (Mac and Quest must be on the same network during use)
- A Meta account (free) and the Meta Horizon app installed on your phone

### Software (installed step-by-step in this guide)

- Homebrew — Mac package manager
- Python 3.11 — required for Shap-E compatibility
- ffmpeg — required for Whisper audio decoding
- Python libraries: FastAPI, Uvicorn, Whisper, Shap-E, Trimesh, PyTorch
- Unity Hub and Unity 6.0 LTS
- Meta XR All-in-One SDK (free, from Unity Asset Store)
- GLTFast (free Unity package for loading 3D files at runtime)

### Estimated Time

| Task | Estimated Time |
|------|---------------|
| Mac server setup (includes model download) | 20–40 minutes |
| Unity project setup | 30–60 minutes |
| Quest configuration and first build | 15–30 minutes |
| Each object generation | 5–10 minutes per object |

---

## Part A: Mac Server Setup

The server is a Python program that listens for requests from your Quest headset, transcribes your voice to text, generates a 3D model, and returns it. Follow these steps in order.

### A1. Install Homebrew

Homebrew is a package manager for macOS. Open **Terminal** (press `Command + Space`, type `Terminal`, press `Enter`) and paste:

```bash
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
```

Press Enter. Type your Mac password when prompted (characters won't appear — this is normal). Follow any on-screen instructions. This takes about 5 minutes.

### A2. Install Python 3.11

Shap-E requires Python 3.10 or 3.11. Install it using Homebrew:

```bash
brew install python@3.11
```

Verify the installation:

```bash
python3.11 --version
```

You should see: `Python 3.11.x`

### A3. Install ffmpeg

Whisper requires ffmpeg to read audio files. Skipping this step causes transcription to fail with a confusing error later.

```bash
brew install ffmpeg
```

> [!TIP]
> ffmpeg is about 200 MB. On a slow connection this may take a few minutes.

### A4. Create the Server Folder

```bash
mkdir -p ~/YourFolder/vr-gen
```

### A5. Copy server.py into the Folder

Locate `server.py` in your project output folder. In Finder, drag it into the `vr-gen` folder on your Desktop.

You should now have: `~/Desktop/vr-gen/server.py`

### A6. Install Python Libraries

Navigate to the folder and install all required libraries. Each command may take several minutes:

```bash
cd ~/YourFolder/vr-gen
```

```bash
pip3.11 install fastapi uvicorn openai-whisper python-multipart trimesh numpy torch --break-system-packages
```

```bash
pip3.11 install git+https://github.com/openai/shap-e.git --break-system-packages
```

> [!WARNING]
> Always use `pip3.11`, not `pip` or `pip3`. Using the wrong pip installs libraries for a different Python version, causing cryptic import errors when the server runs.

### A7. Start the Server for the First Time

Run the server. On first run it downloads the AI models (~1 GB for Shap-E, ~150 MB for Whisper). This takes 10–20 minutes:

```bash
cd ~/YourFolder/vr-gen && python3.11 server.py
```

The server is ready when you see:

```
INFO:     Uvicorn running on http://0.0.0.0:8765 (Press CTRL+C to quit)
```

> [!IMPORTANT]
> **Do not close this Terminal window during your session.** The Quest communicates with this server the entire time. If you accidentally close it, re-run the command — subsequent starts take only 30–60 seconds since models are already downloaded.

### A8. Find Your Mac's Local IP Address

Your Quest needs your Mac's address on the local Wi-Fi network. Open a new Terminal tab (`Command + T`) and run:

```bash
ipconfig getifaddr en0
```

The output will be a number like `192.168.1.42`. **Write this down** — you will enter it into Unity in Part B.

> [!TIP]
> If the command returns nothing, try `en1`: `ipconfig getifaddr en1`

> [!WARNING]
> Your Mac's IP address can change when it reconnects to Wi-Fi. If the app stops connecting on a future day, run this command again and check whether the IP has changed.

### A9. Verify the Server is Working

With the server still running, open a new Terminal tab and run:

```bash
curl http://localhost:8765/health
```

Expected response:

```json
{"status":"ok","backend":"shap-e-local","device":"cpu"}
```

If you see this, the server is fully operational. Proceed to Part B.

---

## Part B: Unity Project Setup

Unity is the development environment for building the Quest app. This section walks through installing Unity, adding required packages, importing project scripts, assembling the scene, and wiring the Inspector settings.

### B1. Download and Install Unity Hub

Unity Hub manages your Unity installations and projects. Download it from:

```
https://unity.com/download
```

Install Unity Hub. When it opens, create a free Unity account if you don't already have one.

### B2. Install Unity 6000.2.10f1

1. In Unity Hub, click **Installs** in the left sidebar, then **Install Editor** (top right).
2. Find **Unity 6000.2.10f1** in the list — the LTS badge means Long-Term Support. Click **Install**.
3. In the modules screen, make sure these are checked:
   - ✅ Android Build Support
   - ✅ Android SDK & NDK Tools
   - ✅ OpenJDK
4. Click **Install**. This downloads ~3 GB and takes 10–20 minutes.

### B3. Create a New Unity Project

1. In Unity Hub, click **Projects** → **New Project**.
2. Select the **3D (URP)** template. URP (Universal Render Pipeline) is required for Meta Quest AR features.
3. Name the project (e.g. `ARObjectGen`) and choose a save location.
4. Click **Create Project**. Unity opens the project — this takes 2–3 minutes the first time.

### B4. Install the Meta XR All-in-One SDK

This free SDK provides all Quest-specific features: controller input, passthrough AR, hand tracking, etc.

1. In Unity, go to **Window → Package Manager**.
2. Click **+** (top left) → **Add package by name**.
3. Type `com.unity.nuget.newtonsoft-json` and click **Add**. Wait for it to install.
4. Open a web browser and go to `assetstore.unity.com`. Search for **Meta XR All-in-One SDK** (free, by Meta Platforms Technologies).
5. Click **Add to My Assets**. If Unity prompts you, click **Open in Unity**.
6. In the Package Manager, click the **My Assets** tab. Find **Meta XR All-in-One SDK** and click **Download**, then **Import**.
7. Accept all prompts. Unity may restart itself — this is normal.
8. After import, go to **Edit → Project Settings → XR Plug-in Management**.
9. Click the **Android tab** (robot icon) and tick the checkbox next to **Oculus**.

### B5. Install GLTFast

GLTFast enables Unity to load `.glb` 3D files (Shap-E's output format) at runtime.

1. Go to **Window → Package Manager**.
2. Click **+** → **Add package by name**.
3. Type `com.atteneder.gltfast` and click **Add**. Wait ~1 minute.

### B6. Import TextMeshPro

1. Go to **Window → TextMeshPro → Import TMP Essential Resources**.
2. Click **Import All** in the dialog that appears.

### B7. Add the Project Scripts

1. In Unity's **Project window** (bottom panel), right-click **Assets** → **Create → Folder**. Name it `Scripts`.
2. In Finder, navigate to your project output folder. Select all `.cs` files.
3. Drag them into **Assets/Scripts/** in Unity's Project window.

**Required files:**

| Script | Purpose |
|--------|---------|
| `VRARManager.cs` | Main controller: voice recording, server calls, ghost placement, inventory |
| `ManipulationManager.cs` | Grab, scale, lock, and delete controller inputs |
| `ObjectManipulator.cs` | Auto-added to each placed object to handle interaction |
| `GenerationProgressBar.cs` | Optional: shows progress bar during generation |
| `ServerClient.cs` | HTTP helper for server communication |
| `SimpleGenTest.cs` | Optional: editor testing without headset (Space bar triggers generation) |

### B8. Configure Android Build Settings

1. Go to **File → Build Settings**.
2. Select **Android** from the platform list. Click **Switch Platform**. Wait 1–2 minutes.
3. Click **Player Settings** (bottom left).
4. In the Inspector, click the **Android tab** (robot icon). Under **Other Settings**, set:

| Setting | Required Value |
|---------|---------------|
| Minimum API Level | Android 10.0 (API level 29) |
| Target API Level | Automatic (highest installed) |
| Scripting Backend | IL2CPP |
| Target Architectures | ARM64 only (uncheck ARMv7) |
| Package Name | `com.yourname.argenerator` (any unique reverse-domain name) |

5. Under **Configuration**, set **Internet Access** to **Required**.

### B9. Configure XR and Passthrough

1. Go to **Edit → Project Settings → XR Plug-in Management**.
2. Click the **Android tab**. Confirm **Oculus** is ticked.
3. In the left sidebar, click **Oculus** (a new sub-section after enabling the plugin).
4. Set **Tracking Origin Mode** to **Floor Level**.
5. Under **Quest Features**, set **Passthrough Support** to **Enabled**.

### B10. Assemble the Scene

#### Remove the Default Camera

In the **Hierarchy** window (top-left), right-click **Main Camera** → **Delete**. The Meta SDK provides its own camera.

#### Add the OVR Camera Rig
##### Option A
1. In the **Project** window, search for `OVRCameraRig`.
2. Drag the **OVRCameraRig** prefab into the Hierarchy.
3. Select it. In the Inspector, find the **OVR Camera Rig** component. Set **Tracking Origin Type** to **Floor Level**.

##### Option B
1. Go to Meta XR Tools -> Building Blocks -> Camera Rig. 

#### Enable Passthrough AR

##### Option A
1. With **OVRCameraRig** selected, click **Add Component** → search `OVRPassthroughLayer` → add it.
2. In the component, tick **Is Underlay**.
3. Expand **OVRCameraRig → TrackingSpace → CenterEyeAnchor** in the Hierarchy.
4. Select **CenterEyeAnchor**. In the **Camera** component, set **Background Type** to **Solid Color**.
5. Click the colour swatch and set all four sliders (R, G, B, A) to **0**. This makes the camera background fully transparent so passthrough shows through.

##### Option B
1. Go to Meta XR Tools -> Building Blocks -> Passthrough. 

#### Create the Gen Manager

1. In the Hierarchy, right-click → **Create Empty**. Rename it `GenManager`.
2. Select it. Click **Add Component** → `VRARManager` → add it.
3. Click **Add Component** again → `ManipulationManager` → add it.

#### Build the UI Canvas

1. Right-click in the Hierarchy → **UI → Canvas**. Rename it `ARCanvas`.
2. In the **Canvas** component, set **Render Mode** to **World Space**.
3. Set **Rect Transform**: Width = `400`, Height = `300`, Scale X/Y/Z = `0.001`. Position = `(0, 1.6, 0.8)`.
4. Right-click `ARCanvas` → **UI → Text - TextMeshPro**. Name it `StatusText`. Font size: `36`, colour: white, alignment: Center/Middle.
5. Repeat to add `TranscriptText` below (font size `28`, lighter colour).
6. Right-click `ARCanvas` → **UI → Panel**. Name it `InventoryPanel`.
7. Right-click `InventoryPanel` → **UI → Scroll View**. Expand until you find **Scroll View → Viewport → Content**. This `Content` object is where inventory buttons appear at runtime.

### B11. Wire the Inspector Fields

Select **GenManager** in the Hierarchy. In the Inspector, fill in the **VRARManager** component:

| Field | What to Put Here | Where to Find It |
|-------|-----------------|-----------------|
| Server Url | `http://YOUR_MAC_IP:8765` | Your IP from Step A8 |
| Right Hand Anchor | Drag: `RightHandAnchor` | OVRCameraRig → TrackingSpace |
| Left Hand Anchor | Drag: `LeftHandAnchor` | OVRCameraRig → TrackingSpace |
| Status Text | Drag: `StatusText` | ARCanvas child |
| Transcript Text | Drag: `TranscriptText` | ARCanvas child |
| Inventory Panel | Drag: `InventoryPanel` | ARCanvas child |
| Inventory Content | Drag: `Content` | InventoryPanel → Scroll View → Viewport → Content |

Now scroll down to the **ManipulationManager** component on the same GenManager:

| Field | What to Put Here | Where to Find It |
|-------|-----------------|-----------------|
| Right Hand Anchor | Drag: `RightHandAnchor` | OVRCameraRig → TrackingSpace |
| Left Hand Anchor | Drag: `LeftHandAnchor` | OVRCameraRig → TrackingSpace |

> [!IMPORTANT]
> Replace `YOUR_MAC_IP` in the Server Url with the actual IP from Step A8. Example: `http://192.168.1.42:8765`. Without the correct IP the app cannot communicate with the server.

### B12. Save the Scene

Press `Command + S`. Name the scene `Main` and click **Save**.

---

## Part C: Meta Quest 3 Setup

### C1. Enable Developer Mode

Developer Mode lets you install custom (sideloaded) apps onto your Quest.

1. Open the **Meta Horizon** app on your phone. Sign in with the same Meta account as your Quest 3.
2. Tap **Menu** (bottom right) → **Devices** → select your Quest 3.
3. Scroll down and tap **Developer Mode** → toggle **On**.
4. If prompted to create a developer organisation: tap **Create Organisation**, enter any name, agree to the terms, confirm. This is free and takes about 1 minute.

> [!TIP]
> Developer Mode only needs to be enabled once. It stays on unless you manually disable it.

### C2. Connect the Quest to Your Mac

1. Connect your Quest 3 to your Mac with a USB-C **data** cable.
2. Put on the headset. A dialog appears: **Allow USB debugging?** Tap **Always allow from this computer**, then **OK**.
3. On your Mac, verify in Terminal:

```bash
adb devices
```

Expected output:

```
List of devices attached
1WMHH81234567890    device
```

If `adb` is not found, install it first:

```bash
brew install android-platform-tools
```

> [!TIP]
> If the Quest shows as `unauthorized` instead of `device`, put on the headset and look for the USB debugging permission dialog.

### C3. Connect Both Devices to the Same Wi-Fi Network

The Quest app communicates with the Mac server over local Wi-Fi during every session.

1. On your Quest: press the Quest button → **Settings** (gear icon) → **Wi-Fi** → connect to your network.
2. On your Mac: confirm you're on the same network (same SSID).

> [!WARNING]
> If your router broadcasts both 2.4 GHz and 5 GHz as separate SSIDs (e.g. `MyNetwork` and `MyNetwork_5G`), make sure both devices are on the **same one**. Devices on different bands can fail to communicate even though both appear "connected".

---

## Part D: Build and Deploy

### D1. Build and Install on Your Quest

1. In Unity, go to **File → Build Settings**.
2. Confirm **Android** is the active platform (Unity logo appears next to it).
3. Confirm your scene appears under **Scenes In Build**. If not, click **Add Open Scenes**.
4. From the **Run Device** dropdown, select your Quest 3. (If it doesn't appear, check the USB connection and re-run `adb devices`.)
5. Click **Build And Run**. Choose a save location for the `.apk` file.
6. Unity compiles the app. **First build: 10–20 minutes.** Subsequent builds: 2–5 minutes.
7. When complete, Unity automatically installs and launches the app on your Quest.

### D2. Launching the App in Future Sessions

After the first build, the app stays on your Quest. Only rebuild when you change Unity scripts or the scene.

To launch without rebuilding:

1. Press the Quest button to open the universal menu.
2. Navigate to **App Library** (grid icon).
3. Click the dropdown at the top right → **Unknown Sources**.
4. Your app appears here. Tap it to launch.

---

## Part E: Using the App

### E1. Start the Server Before Every Session

Before putting on your Quest, always start the Mac server first:

```bash
cd ~/Desktop/vr-gen && python3.11 server.py
```

Wait until Terminal shows:

```
INFO:     Uvicorn running on http://0.0.0.0:8765 (Press CTRL+C to quit)
```

Keep this Terminal window open for the entire session.

### E2. Launch the App and Check the Status

Put on your Quest 3 and launch the app from **App Library → Unknown Sources**. After a few seconds you will see your real room through passthrough, with a floating white text panel reading:

```
Hold [Right Trigger] to speak and generate
```

### E3. Generate a 3D Object

1. **Hold the Right Trigger** (index-finger button on the right controller). The panel shows: `Recording...`
2. **Speak clearly** while holding. Include colour and material for best results:
   - ✅ *"a blue ceramic vase"*
   - ✅ *"a small red wooden table"*
   - ✅ *"a grey metal sphere"*
3. **Release the trigger**. The panel shows `Transcribing...` then your words appear as confirmation.
4. The status changes to `Generating: [your prompt] (5-10 min on CPU)`. **Wait patiently.**
5. When generation completes, a semi-transparent **ghost** object appears in the scene.
6. **Point your right controller** at a surface. The ghost follows your aim.
7. **Pull Right Trigger** to place the object. It becomes solid and takes on its colour.

> [!TIP]
> Every placed object is saved to the inventory. You can re-spawn any object later without re-generating — it's instant.

### E4. Interacting with Placed Objects

| Input | What Happens |
|-------|-------------|
| Right Grip — hold | Grabs the aimed object (turns green). Move hand = translate, rotate wrist = rotate. |
| Right Grip + Left Grip — both held | Two-hand scale: hands apart = bigger, hands together = smaller. |
| Release Right Grip | Drops the object in its current position. |
| A Button (while holding) | Locks object in place (turns blue). Cannot move accidentally. Grab again to unlock. |
| B Button (while holding) | Permanently deletes the grabbed object. |

### E5. Using the Inventory

1. Press the **Left Menu Button** to open the inventory panel.
2. Use the **Right Thumbstick ↑ / ↓** to scroll the list. Selected item is highlighted blue.
3. Pull **Right Trigger** to re-spawn the highlighted object as a ghost — no re-generation, instant.
4. Aim and pull **Right Trigger** again to place it as a fresh copy.

---

## Part F: Controls Reference

### Generation & Placement

| Input | Action |
|-------|--------|
| Right Trigger — hold then release | Record voice → transcribe → generate 3D object |
| Right Trigger — press (ghost visible) | Confirm placement of the ghost object |

### Object Manipulation

| Input | Action |
|-------|--------|
| Right Grip (hand trigger) — hold | Grab object. Green tint = grabbed. Move = translate, rotate wrist = rotate. |
| Right Grip + Left Grip — both held | Two-hand scale: apart = bigger, together = smaller |
| A Button (while holding) | Lock object in place. Blue tint = locked. Grab again to unlock. |
| B Button (while holding) | Delete grabbed object permanently |

### Inventory

| Input | Action |
|-------|--------|
| Left Menu Button | Toggle inventory panel open/closed |
| Right Thumbstick ↑ / ↓ (inventory open) | Navigate list of generated objects |
| Right Trigger (inventory open) | Re-spawn selected item instantly (no re-generation) |

### Object Visual States

| State | Colour Cue | Meaning |
|-------|-----------|---------|
| Ghost | Translucent blue tint | In placement mode. Aim with right controller and pull Right Trigger to place. |
| Default | Object's colour | Placed and ready. Point and hold Right Grip to grab. |
| Grabbed | Green tint | Being held. Follows right hand. Release grip to drop. |
| Locked | Blue tint | Saved in position. Won't move. Grab again to unlock. |

---

## Part G: Troubleshooting

### App shows: "Generation failed. Check server logs."

- Confirm the Mac server is running: Terminal must show `Uvicorn running`.
- Check that the **Server Url** in VRARManager matches your Mac's current IP exactly. Example: `http://192.168.1.42:8765` with no trailing slash.
- Confirm both Mac and Quest are on the **same Wi-Fi network**.
- Look in Terminal for red error messages after you triggered generation.

### App shows: "Couldn't transcribe. Try again."

- Ensure ffmpeg is installed: `brew install ffmpeg`
- Hold the Right Trigger for at least **2–3 seconds** while speaking clearly.
- In Terminal, look for a line containing `ffmpeg not found` or `transcription failed`.

### App shows: "Requesting microphone permission..." and stays there

- Put on the headset — a system dialog may be waiting for you to tap **Allow Microphone Access**.
- Go to **Settings → Privacy → Microphone** on the Quest and enable it for Unknown Sources apps.
- Uninstall and reinstall the app to trigger the permission dialog again.

### Server crashes with: "ModuleNotFoundError"

- Re-run both install commands from Step A6 using `pip3.11` exactly as written.
- Confirm you are in the correct folder: `cd ~/Desktop/vr-gen` before running the server.

### Server takes a very long time to start

On the **very first run**, Shap-E downloads ~1 GB of model weights and Whisper downloads ~150 MB. This can take 10–30 minutes. This only happens once — subsequent starts take 30–60 seconds.

### `adb devices` shows nothing or "unauthorized"

- Make sure your USB-C cable supports data transfer. Try a different cable.
- Put on the Quest and look for the USB debugging dialog — tap **Always allow from this computer**.
- Unplug and replug the cable.
- If `adb` is not found: `brew install android-platform-tools`

### Quest cannot reach the Mac server

- Your Mac's IP may have changed. Run `ipconfig getifaddr en0` to check. If it changed, update **Server Url** in VRARManager and rebuild the app.
- To prevent this in future, assign your Mac a **static local IP** through your router's admin panel.

### Unity build fails

- Check **Window → General → Console** in Unity for the full error message.
- Go to **Edit → Preferences → External Tools** and confirm JDK, SDK, and NDK paths are set.
- In Unity Hub, click the three-dot menu on your 6000.2 install → **Add Modules** → confirm Android Build Support, Android SDK & NDK Tools, and OpenJDK are installed.

### Objects appear underground or at wrong scale

- **Underground**: check that **Tracking Origin Type** on OVRCameraRig is **Floor Level** (not Eye Level).
- **Wrong scale**: use Right Grip to grab and both Grips to scale. Auto-scale targets 0.3 m but Shap-E geometry varies.

---

## Part H: File Reference

### Server File

| File | Purpose |
|------|---------|
| `server.py` | Mac server running on port `8765`. Provides `/health`, `/transcribe` (Whisper voice-to-text), `/generate` (Shap-E text-to-3D, returns `.glb`), `/progress` (generation progress 0–1). Must run throughout the session. |

### Unity Scripts

| Script | Purpose |
|--------|---------|
| `VRARManager.cs` | Core controller. Full pipeline: voice recording → Whisper → Shap-E → ghost display → placement. Manages in-session inventory with instant re-spawn. |
| `ManipulationManager.cs` | All physical interaction: right grip to grab (translate + rotate), both grips to scale, A to lock, B to delete. Reads `VRARManager.InventoryOpen` to avoid input conflicts. |
| `ObjectManipulator.cs` | Auto-added to every placed object. Accepts Grab/Release/Lock/ScaleBy from ManipulationManager. Adds BoxCollider for raycasting if missing. Green tint = grabbed, blue = locked. |
| `GenerationProgressBar.cs` | Optional. Polls `/progress` every 2 seconds, updates a UI Slider showing 0–100% during generation. |
| `ServerClient.cs` | Low-level HTTP helpers used internally by VRARManager. |
| `SimpleGenTest.cs` | Editor-only. Type a prompt in the Inspector, press Space in Play mode to generate — useful for testing without wearing the headset. |

### Scene Hierarchy

```
Scene
├── OVRCameraRig
│   └── TrackingSpace
│       ├── LeftHandAnchor          ← drag to "Left Hand Anchor" in both components
│       ├── RightHandAnchor         ← drag to "Right Hand Anchor" in both components
│       └── CenterEyeAnchor         ← Camera: Background = Solid Color, Alpha = 0
├── GenManager  (empty GameObject)
│   ├── [VRARManager component]     ← wire all Inspector fields
│   └── [ManipulationManager]       ← wire hand anchor fields
├── ARCanvas  (World Space · scale 0.001 · pos 0 / 1.6 / 0.8)
│   ├── StatusText  (TextMeshPro)
│   ├── TranscriptText  (TextMeshPro)
│   └── InventoryPanel
│       └── Scroll View → Viewport → Content  ← drag to "Inventory Content"
└── EventSystem
```

---

## Quick Reference Card

### Session Start Checklist

- [ ] Mac server running: `cd ~/Desktop/vr-gen && python3.11 server.py`
- [ ] Terminal shows: `Uvicorn running on http://0.0.0.0:8765`
- [ ] Mac and Quest on the **same Wi-Fi network**
- [ ] App launched from App Library → Unknown Sources
- [ ] Status text visible in headset: *Hold [Right Trigger] to speak and generate*

### Controls Cheat Sheet

| Input | Action |
|-------|--------|
| Right Trigger — hold/release | 🎤 Record voice and generate object |
| Right Trigger — press (ghost visible) | 📌 Place object |
| Right Grip — hold | ✋ Grab object (move + rotate with hand) |
| Right Grip + Left Grip | ↔️ Two-hand scale |
| A Button (holding object) | 🔒 Lock in place (blue = locked) |
| B Button (holding object) | 🗑️ Delete object |
| Left Menu Button | 📋 Open/close inventory |
| Thumbstick ↑↓ (inventory open) | Navigate items |
| Right Trigger (inventory open) | ♻️ Re-spawn selected item instantly |

---

*AR Object Generator — Meta Quest 3 + Shap-E + Whisper + Unity 6000.2*
