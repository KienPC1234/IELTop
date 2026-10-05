"""Fetch the Piper examiner voice and a portable espeak-ng.

Runs once on a dev machine, not in the app:
    python fetch_models.py

Downloads en_US-lessac-medium (MIT, rhasspy/piper-voices) and the
espeak-ng 1.52.0 Windows build, extracts the portable exe plus voice
data, and runs a test synthesis. Copy the results into
Content/Assets/Models/ as tts-piper-lessac-medium.onnx,
tts-piper-lessac-medium.onnx.json, and the espeak-ng folder.
"""

import json
import shutil
import subprocess
import urllib.request
from pathlib import Path

VOICE = "en_US-lessac-medium"
VOICE_BASE = f"https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/{VOICE}"
ESPEAK_MSI = "https://github.com/espeak-ng/espeak-ng/releases/download/1.52.0/espeak-ng.msi"

HERE = Path(__file__).resolve().parent
OUT = HERE / "models"
ESPEAK_DIR = OUT / "espeak-ng"


def download(url: str, dst: Path) -> None:
    print(f"fetch {url}")
    urllib.request.urlretrieve(url, dst)


def fetch_voice() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    download(f"{VOICE_BASE}.onnx", OUT / "tts-piper-lessac-medium.onnx")
    download(f"{VOICE_BASE}.onnx.json", OUT / "tts-piper-lessac-medium.onnx.json")
    config = json.loads((OUT / "tts-piper-lessac-medium.onnx.json").read_text(encoding="utf-8"))
    assert "phoneme_id_map" in config, "voice config has no phoneme map"
    print(f"voice ok: {config['dataset']}, {config['audio']['sample_rate']} Hz")


def fetch_espeak() -> Path:
    msi = OUT / "espeak-ng.msi"
    download(ESPEAK_MSI, msi)
    target = OUT / "espeak-extract"
    subprocess.run(
        ["msiexec", "/a", str(msi), "/qn", f"TARGETDIR={target}"],
        check=True,
    )
    exe = next(target.rglob("espeak-ng.exe"))
    ESPEAK_DIR.mkdir(parents=True, exist_ok=True)
    shutil.copy(exe, ESPEAK_DIR / "espeak-ng.exe")
    # espeak-ng.exe is a thin launcher: without libespeak-ng.dll next to it the
    # process exits 0xC0000135 (DLL not found) and the voice goes silent. Copy
    # every DLL from the install folder, not only the exe.
    for dll in exe.parent.glob("*.dll"):
        shutil.copy(dll, ESPEAK_DIR / dll.name)
    shutil.copytree(exe.parent / "espeak-ng-data", ESPEAK_DIR / "espeak-ng-data", dirs_exist_ok=True)
    print(f"espeak ok: {ESPEAK_DIR / 'espeak-ng.exe'} (+ {len(list(exe.parent.glob('*.dll')))} dll)")
    return ESPEAK_DIR / "espeak-ng.exe"


def test_voice(exe: Path) -> None:
    import numpy as np
    import onnxruntime as ort

    env = {"ESPEAK_DATA_PATH": str(ESPEAK_DIR / "espeak-ng-data")}
    out = subprocess.run(
        [str(exe), "-v", "en-us", "-q", "--ipa=2", "-x", "Hello world, this is a test."],
        capture_output=True, text=True, env={**dict(__import__("os").environ), **env},
        check=True,
    ).stdout
    config = json.loads((OUT / "tts-piper-lessac-medium.onnx.json").read_text(encoding="utf-8"))
    pmap = config["phoneme_id_map"]
    session = ort.InferenceSession(str(OUT / "tts-piper-lessac-medium.onnx"), providers=["CPUExecutionProvider"])
    total = 0
    for line in out.split("\n"):
        line = line.strip().replace("_", " ")
        if not line:
            continue
        ids = [pmap["^"][0], pmap["_"][0]]
        for ch in line:
            if ch in pmap:
                ids.extend(pmap[ch])
            ids.append(pmap["_"][0])
        ids.append(pmap["$"][0])
        audio = session.run(
            None,
            {
                "input": np.array([ids], dtype=np.int64),
                "input_lengths": np.array([len(ids)], dtype=np.int64),
                "scales": np.array(
                    [config["inference"]["noise_scale"], config["inference"]["length_scale"], config["inference"]["noise_w"]],
                    dtype=np.float32,
                ),
            },
        )[0]
        total += audio.size
    seconds = total / config["audio"]["sample_rate"]
    assert seconds > 1.0, "synthesis produced almost no audio"
    print(f"synthesis ok: {seconds:.1f} seconds of audio")


def main() -> None:
    fetch_voice()
    exe = fetch_espeak()
    test_voice(exe)
    print("Copy tts-piper-lessac-medium.onnx, tts-piper-lessac-medium.onnx.json,")
    print("and the espeak-ng folder into Content/Assets/Models/.")


if __name__ == "__main__":
    main()
