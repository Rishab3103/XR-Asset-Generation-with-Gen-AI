"""
download_models.py
------------------
Run this ONCE to download free CC0/MIT 3D models into the models/ folder.
After this, the server works fully offline with no API keys needed.

Run:
  python download_models.py
"""

import requests
from pathlib import Path

MODELS_DIR = Path(__file__).parent / "models"
MODELS_DIR.mkdir(exist_ok=True)

BASE = "https://github.com/KhronosGroup/glTF-Sample-Assets/raw/main/Models"

# keyword -> glb filename and URL
# Each entry: (filename, url)
DOWNLOADS = {
    "lamp":     ("lamp.glb",     BASE + "/Lantern/glTF-Binary/Lantern.glb"),
    "lantern":  ("lamp.glb",     BASE + "/Lantern/glTF-Binary/Lantern.glb"),
    "light":    ("lamp.glb",     BASE + "/Lantern/glTF-Binary/Lantern.glb"),
    "bottle":   ("bottle.glb",   BASE + "/WaterBottle/glTF-Binary/WaterBottle.glb"),
    "water":    ("bottle.glb",   BASE + "/WaterBottle/glTF-Binary/WaterBottle.glb"),
    "duck":     ("duck.glb",     BASE + "/Duck/glTF-Binary/Duck.glb"),
    "bird":     ("duck.glb",     BASE + "/Duck/glTF-Binary/Duck.glb"),
    "fox":      ("fox.glb",      BASE + "/Fox/glTF-Binary/Fox.glb"),
    "animal":   ("fox.glb",      BASE + "/Fox/glTF-Binary/Fox.glb"),
    "fish":     ("fish.glb",     BASE + "/BarramundiFish/glTF-Binary/BarramundiFish.glb"),
    "helmet":   ("helmet.glb",   BASE + "/DamagedHelmet/glTF-Binary/DamagedHelmet.glb"),
    "hat":      ("helmet.glb",   BASE + "/DamagedHelmet/glTF-Binary/DamagedHelmet.glb"),
    "car":      ("car.glb",      BASE + "/CesiumMilkTruck/glTF-Binary/CesiumMilkTruck.glb"),
    "truck":    ("car.glb",      BASE + "/CesiumMilkTruck/glTF-Binary/CesiumMilkTruck.glb"),
    "vehicle":  ("car.glb",      BASE + "/CesiumMilkTruck/glTF-Binary/CesiumMilkTruck.glb"),
    "camera":   ("camera.glb",   BASE + "/AntiqueCamera/glTF-Binary/AntiqueCamera.glb"),
    "robot":    ("robot.glb",    BASE + "/RobotExpressive/glTF-Binary/RobotExpressive.glb"),
    "person":   ("robot.glb",    BASE + "/RobotExpressive/glTF-Binary/RobotExpressive.glb"),
    "human":    ("robot.glb",    BASE + "/RobotExpressive/glTF-Binary/RobotExpressive.glb"),
    "character":("robot.glb",    BASE + "/RobotExpressive/glTF-Binary/RobotExpressive.glb"),
    "sofa":     ("sofa.glb",     BASE + "/GlamVelvetSofa/glTF-Binary/GlamVelvetSofa.glb"),
    "couch":    ("sofa.glb",     BASE + "/GlamVelvetSofa/glTF-Binary/GlamVelvetSofa.glb"),
    "chair":    ("sofa.glb",     BASE + "/GlamVelvetSofa/glTF-Binary/GlamVelvetSofa.glb"),
    "seat":     ("sofa.glb",     BASE + "/GlamVelvetSofa/glTF-Binary/GlamVelvetSofa.glb"),
    "shoe":     ("shoe.glb",     BASE + "/MaterialsVariantsShoe/glTF-Binary/MaterialsVariantsShoe.glb"),
    "boot":     ("shoe.glb",     BASE + "/MaterialsVariantsShoe/glTF-Binary/MaterialsVariantsShoe.glb"),
    "sneaker":  ("shoe.glb",     BASE + "/MaterialsVariantsShoe/glTF-Binary/MaterialsVariantsShoe.glb"),
    "clock":    ("clock.glb",    BASE + "/SunflowerClock/glTF-Binary/SunflowerClock.glb"),
    "watch":    ("clock.glb",    BASE + "/SunflowerClock/glTF-Binary/SunflowerClock.glb"),
    "avocado":  ("avocado.glb",  BASE + "/Avocado/glTF-Binary/Avocado.glb"),
    "food":     ("avocado.glb",  BASE + "/Avocado/glTF-Binary/Avocado.glb"),
    "fruit":    ("avocado.glb",  BASE + "/Avocado/glTF-Binary/Avocado.glb"),
    "box":      ("box.glb",      BASE + "/Box/glTF-Binary/Box.glb"),
    "cube":     ("box.glb",      BASE + "/Box/glTF-Binary/Box.glb"),
    "crate":    ("box.glb",      BASE + "/Box/glTF-Binary/Box.glb"),
    "chest":    ("box.glb",      BASE + "/Box/glTF-Binary/Box.glb"),
    "barrel":   ("box.glb",      BASE + "/Box/glTF-Binary/Box.glb"),
    "building": ("box.glb",      BASE + "/Box/glTF-Binary/Box.glb"),
    "house":    ("box.glb",      BASE + "/Box/glTF-Binary/Box.glb"),
    "default":  ("box.glb",      BASE + "/Box/glTF-Binary/Box.glb"),
}

# Collect unique files to download
unique_files = {}
for keyword, (filename, url) in DOWNLOADS.items():
    unique_files[filename] = url

print("Downloading " + str(len(unique_files)) + " model files into: " + str(MODELS_DIR))
print()

for filename, url in unique_files.items():
    dest = MODELS_DIR / filename
    if dest.exists():
        print("  Already exists: " + filename)
        continue
    print("  Downloading " + filename + " ...")
    try:
        r = requests.get(url, timeout=30, allow_redirects=True)
        r.raise_for_status()
        dest.write_bytes(r.content)
        print("  OK (" + str(round(len(r.content)/1024)) + " KB)")
    except Exception as e:
        print("  FAILED: " + str(e))

# Write keyword -> filename mapping for the server
mapping_path = MODELS_DIR / "keywords.txt"
with open(mapping_path, "w") as f:
    for keyword, (filename, _) in DOWNLOADS.items():
        f.write(keyword + "=" + filename + "\n")

print()
print("Done! Run: python server.py")
