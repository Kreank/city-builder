"""Schreibt neben jedes FBX unter Assets/_Project/Models eine Beschreibungsdatei <Name>.json (Vorgabe aus dem
Spielprojekt, s. docs/Modell-Konvention.md):
  Gebaeude          {"role":"building","zone":"<ZoneType>","level":1..3}
  Strassenausstattung {"role":"prop","placement":"sidewalk"}
  Baeume            {"role":"tree","biome":"temperate"}
  Dienste           {"role":"service","service":"<ServiceKinds>"}
Nur diese vier Rollen sind vorgegeben. Fuer alle anderen Modelle wird KEINE JSON geschrieben (dann gelten die
Ordnerregeln bzw. die Liste _Unused.txt des Spiels); eine veraltete JSON daneben wird entfernt.

Stufe (level) der Gebaeude: je Zone nach Modellhoehe gedrittelt, wie BuildingView.PickForLevel bisher auswaehlt
(MaxLevel = 3, LandValueRules). Hoehe = Bounding Box des importierten FBX.

Aufruf: blender --background --factory-startup --python write_sidecars.py"""
import os, sys, json
import bpy
from mathutils import Vector

MODELS = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Project", "Models"))
MAX_LEVEL = 3
ZONES = ("ResidentialLow", "ResidentialMedium", "ResidentialHigh", "Mixed", "Retail", "Office", "Industry")

# Dienste: Dateiname unter Models/Services -> ServiceKinds (Simulation/Services). Klaeranlage ist Versorgung
# (FacilityFiles "Services/SewagePlant"), deshalb hier nicht aufgefuehrt.
SERVICES = {"PoliceStation": "police", "FireStation": "fire", "Hospital": "health", "Clinic": "health",
            "School": "education", "PrimarySchool": "education", "HighSchool": "education", "Library": "library",
            "Park": "park", "ParkSmall": "park", "ParkLarge": "park", "Landfill": "garbage", "Incinerator": "garbage",
            "Newspaper": "media", "RadioStation": "media", "TVTower": "media"}
# Props, die an den Gehweg gehoeren. BusStop fehlt absichtlich: ist als OePNV-Haltestelle gemappt (TransportFiles).
SIDEWALK = {"Bench", "TrashBin", "StreetLamp", "Hydrant", "Bollard", "BikeRack", "Mailbox", "AdPillar", "Kiosk",
            "Planter", "PicnicTable"}


def model_height(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    zs = []
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            for c in o.bound_box:
                zs.append((o.matrix_world @ Vector(c)).z)
    return (max(zs) - min(zs)) if zs else 0.0


def collect():
    out = {}
    for root, _, files in os.walk(MODELS):
        for f in files:
            if f.lower().endswith(".fbx"):
                rel = os.path.relpath(os.path.join(root, f), MODELS).replace("\\", "/")
                out[rel] = os.path.join(root, f)
    return out


def main():
    fbx = collect()
    meta = {}
    by_zone = {}
    for rel, path in fbx.items():
        parts = rel.split("/")
        name = os.path.splitext(parts[-1])[0]
        if parts[0] == "Buildings" and len(parts) == 3 and parts[1] in ZONES:
            by_zone.setdefault(parts[1], []).append((rel, path))
        elif parts[0] == "Services" and name in SERVICES:
            meta[rel] = {"role": "service", "service": SERVICES[name]}
        elif parts[0] == "Props" and name in SIDEWALK:
            meta[rel] = {"role": "prop", "placement": "sidewalk"}
        elif parts[0] == "Nature" and name.startswith("Tree_"):
            meta[rel] = {"role": "tree", "biome": "temperate"}
    for zone, items in by_zone.items():
        hs = sorted(((model_height(p), rel) for rel, p in items), key=lambda t: (t[0], t[1]))
        n = len(hs)
        for i, (h, rel) in enumerate(hs):
            # Index i liegt im Drittel lv, wenn n*(lv-1)//3 <= i < n*lv//3 (gleiche Aufteilung wie PickForLevel)
            lv = next(l for l in range(1, MAX_LEVEL + 1) if i < max(n * (l - 1) // MAX_LEVEL + 1, n * l // MAX_LEVEL))
            meta[rel] = {"role": "building", "zone": zone, "level": lv}
            print("CB_LEVEL %-40s h=%6.2f level=%d" % (rel, h, lv))
    written = removed = 0
    for rel, path in fbx.items():
        jpath = os.path.splitext(path)[0] + ".json"
        if rel in meta:
            with open(jpath, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(json.dumps(meta[rel], ensure_ascii=False) + "\n")
            written += 1
        elif os.path.exists(jpath):
            os.remove(jpath)
            removed += 1
    print("CB_SIDECARS written=%d removed=%d fbx=%d" % (written, removed, len(fbx)))


main()
