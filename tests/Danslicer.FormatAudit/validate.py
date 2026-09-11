"""Independent export validation using an external UVtoolsCmd and Pillow.
Run the C# audit generator first, then: python validate.py OUTPUT UVTOOLS_CMD
No UVtools library is loaded by Danslicer; the CLI only reads generated exports.
"""
import hashlib
import json
import pathlib
import re
import subprocess
import sys
import zipfile
from PIL import Image

Image.MAX_IMAGE_PIXELS = 200_000_000
root = pathlib.Path(sys.argv[1]).resolve()
cli = pathlib.Path(sys.argv[2]).resolve()
expected = json.loads((root / "expected.json").read_text())
results = []
for profile in expected:
    folder = root / "extracted" / profile["id"]
    folder.mkdir(parents=True, exist_ok=True)
    # Always validate a fresh extraction; never accept outputs left by a previous executable.
    if any(folder.iterdir()):
        raise RuntimeError(f"Extraction folder must be empty: {folder}")
    call = subprocess.run([str(cli), "--no-progress", "extract", str(root / profile["filename"]), str(folder)], capture_output=True, text=True, timeout=180)
    (folder / "cli.log").write_text(call.stdout + call.stderr, encoding="utf-8")
    decoded_folder = folder / "decoded"
    decode = subprocess.run([str(cli), "--no-progress", "extract", str(root / profile["filename"]), str(decoded_folder), "-c", "Layers"], capture_output=True, text=True, timeout=180)
    (folder / "decode.log").write_text(decode.stdout + decode.stderr, encoding="utf-8")
    errors = []
    try:
        metadata = (folder / "Layers.ini").read_text(encoding="utf-8-sig")
        configuration = (folder / "Configuration.ini").read_text(encoding="utf-8-sig")
        fields = dict(re.findall(r"^([^=\r\n]+?)\s*=\s*([^\r\n]*)", configuration, re.M))
        def field(*names):
            for name in names:
                if name in fields:
                    return float(fields[name])
            raise AssertionError(f"Missing metadata field: {names}")
        def close(actual, desired, label):
            assert abs(actual - desired) < max(.001, abs(desired) * .00001), f"{label}: {actual}, expected {desired}"
        kind = profile["format"]
        if kind not in ("photon-workshop", "anet", "lgs"):
            close(field("DisplayWidth", "BedSizeX", "MachineX", "PlatformXSize"), profile["displayWidth"], "Display width")
            close(field("DisplayHeight", "BedSizeY", "MachineY", "PlatformYSize"), profile["displayHeight"], "Display height")
        if kind == "lgs":
            close(field("PixelPerMmX"), profile["width"] / profile["displayWidth"], "X scale")
            close(field("PixelPerMmY"), profile["height"] / profile["displayHeight"], "Y scale")
        if kind in ("goo", "ctb", "ctb-encrypted", "cxdlp", "cxdlp-v4", "phz", "lgs"):
            scale = 60 if kind == "cxdlp" else 1
            close(field("LiftHeight"), profile["liftHeight"], "Lift height")
            close(field("BottomLiftHeight"), profile["bottomLiftHeight"], "Bottom lift height")
            close(field("LiftSpeed") * scale, profile["liftSpeed"], "Lift speed")
            close(field("BottomLiftSpeed") * scale, profile["bottomLiftSpeed"], "Bottom lift speed")
            if kind != "lgs":
                close(field("RetractSpeed") * scale, profile["retractSpeed"], "Retract speed")
        sections = {int(m[1]): m[2] for m in re.finditer(r"\[(\d+)\]\s*([^\[]*)", metadata)}
        assert len(sections) == len(profile["layers"]), f"Layer count: {len(sections)}"
        cws_exposures = None
        if profile["format"] in ("cws", "cws-rgb"):
            # UVtools 6.2's parser drops ;<Delay> commands, including in its own exports.
            # Validate actual archive commands independently; never rewrite the file to suit that bug.
            with zipfile.ZipFile(root / profile["filename"]) as archive:
                program = archive.read("danslicer.gcode").decode()
                cws_exposures = [float(v) / 1000 for v in re.findall(r"M106 S255\s*;<Delay> ([\d.]+)\s*M106 S0", program)]
                assert len(cws_exposures) == len(profile["layers"]), "Missing CWS cure command"
        for i, digest in enumerate(profile["layers"]):
            block = sections[i]
            exposure = cws_exposures[i] if cws_exposures is not None else float(re.search(r"ExposureTime: ([\d.]+)", block)[1])
            assert abs(exposure - (profile["bottomExposure"] if i < profile["bottomLayers"] else profile["exposure"])) < .001, f"Exposure layer {i}: {exposure}"
            z = float(re.search(r"PositionZ: ([\d.]+)", block)[1])
            assert abs(z - (i + 1) * profile["layerHeight"]) < .0001, f"Z layer {i}: {z}"
            if kind in ("ctb-encrypted", "cxdlp-v4", "cws", "cws-rgb", "chitu-zip") and not profile.get("firmwareControlsPeel", False):
                for label, key in (("LiftHeight", "liftHeight"), ("LiftSpeed", "liftSpeed"), ("RetractSpeed", "retractSpeed")):
                    desired = profile[("bottom" + key[0].upper() + key[1:]) if i < profile["bottomLayers"] and key != "retractSpeed" else key]
                    close(float(re.search(label + r": ([\d.]+)", block)[1]), desired, f"Layer {i} {label}")
            image = decoded_folder / f"layer{i}.png"
            if image.exists():
                with Image.open(image) as decoded:
                    assert decoded.size == (profile["width"], profile["height"]), f"Resolution: {decoded.size}"
                    actual = hashlib.sha256(decoded.convert("L").tobytes()).hexdigest().upper()
            else:
                # UVtools omits empty PNGs in File extraction, but records their decoded pixel count.
                assert "NonZeroPixelCount: 0\n" in block, f"Missing nonempty layer {i}"
                actual = hashlib.sha256(bytes(profile["width"] * profile["height"])).hexdigest().upper()
            assert actual == digest, f"Pixel hash differs on layer {i}: {actual}"
        assert list(folder.glob("Thumbnail*.png")) or list(folder.rglob("thumbnail*.png")) or (folder / "preview.png").exists() or profile["format"] in ("cws", "cws-rgb"), "No decoded previews"
    except Exception as error:
        errors.append(str(error))
    result = {**profile, "passed": not errors, "errors": errors, "cliExit": call.returncode,
              "exposureValidation": "archive G-code (UVtools 6.2 CWS reader bug reproduced on its own output)" if profile["format"] in ("cws", "cws-rgb") else "UVtools decoded layer metadata"}
    results.append(result)
    print(("PASS " if not errors else "FAIL ") + profile["id"] + (" : " + "; ".join(errors) if errors else ""), flush=True)
    (root / "validation.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
sys.exit(1 if any(not row["passed"] for row in results) else 0)
