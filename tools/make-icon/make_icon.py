"""IELTop logo and icon generator.

The source of truth is the symbol art, an SVG:
`Content/Assets/Images/IELTop-red-symbol.svg`. The red rounded square is part of
that art, so the icon keeps the same shape at every size. The symbol reads
clearly down to 16 px, unlike the wordmark, so it is what the app icon uses.

Reads the SVG and writes the sizes the app ships: a multi size Windows icon, a
256 px logo for docs and the store, and a 64 px logo for the README.

Run from the repository root, in this folder's conda env:

    conda env create -f environment.yml
    conda run -n ieltop-icon python make_icon.py

`resvg` renders the SVG. It comes from the same environment, so no browser or
system SVG library is needed.
"""

from __future__ import annotations

import io
import shutil
import struct
import subprocess
import tempfile
from pathlib import Path

from PIL import Image

REPO_ROOT = Path(__file__).resolve().parents[2]
IMAGES = REPO_ROOT / "Content" / "Assets" / "Images"

SOURCE = IMAGES / "IELTop-red-symbol.svg"
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]


def render_source() -> Image.Image:
    """Rasterizes the source SVG at 256 px, the largest size that is needed."""
    if not SOURCE.exists():
        raise SystemExit(f"Logo source not found: {SOURCE}")

    resvg = shutil.which("resvg")
    if resvg is None:
        raise SystemExit(
            "resvg was not found. Run this script with "
            "'conda run -n ieltop-icon python make_icon.py'."
        )

    with tempfile.TemporaryDirectory() as tmp:
        out = Path(tmp) / "logo.png"
        subprocess.run(
            [resvg, "--width", "256", str(SOURCE), str(out)],
            check=True,
        )
        return Image.open(out).convert("RGBA")


def build_ico(source: Image.Image) -> None:
    """Write a multi size .ico, one PNG frame per size, in size order."""
    pngs: list[bytes] = []
    for size in ICO_SIZES:
        frame = source.resize((size, size), Image.LANCZOS)
        buffer = io.BytesIO()
        frame.save(buffer, "PNG")
        pngs.append(buffer.getvalue())

    header = struct.pack("<HHH", 0, 1, len(ICO_SIZES))
    offset = 6 + 16 * len(ICO_SIZES)
    entries = b""
    blobs = b""
    for size, png in zip(ICO_SIZES, pngs):
        dim = 0 if size >= 256 else size  # 0 means 256 in the ICO header.
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(png), offset)
        blobs += png
        offset += len(png)

    (IMAGES / "app.ico").write_bytes(header + entries + blobs)


def main() -> None:
    source = render_source()

    source.resize((256, 256), Image.LANCZOS).save(IMAGES / "logo-256.png", "PNG")
    source.resize((64, 64), Image.LANCZOS).save(IMAGES / "logo-64.png", "PNG")
    build_ico(source)

    print("Wrote app.ico, logo-256.png, logo-64.png to", IMAGES)
    print("Icon sizes:", ", ".join(str(s) for s in ICO_SIZES))


if __name__ == "__main__":
    main()
