"""Export an English whisper model to ONNX (INT8) for offline transcription.

Runs once on a dev machine, not in the app. Pick the size you want:
    base (balanced)   small (more accurate, slower)

    optimum-cli export onnx --model openai/whisper-<size>.en \
        --task automatic-speech-recognition --output ./models/fp32
    python export_onnx.py <size>

tiny is no longer used: it was too weak and has been removed from the app.
It copies stt-whisper-<size>-en-encoder-int8.onnx,
stt-whisper-<size>-en-decoder-int8.onnx, and stt-whisper-<size>-en-vocab.json
into Content/Assets/Models/. The app auto-selects the largest installed encoder
(see OnnxModelRegistry), so copying a bigger one is all it takes to use it.
"""

import json
import sys
import urllib.request
from pathlib import Path

from onnxruntime.quantization import QuantType, quantize_dynamic

SIZE = (sys.argv[1] if len(sys.argv) > 1 else "base").lower()
MODEL_ID = f"openai/whisper-{SIZE}.en"
LICENSE = "MIT"
HERE = Path(__file__).resolve().parent
import os
FP32 = HERE / "models" / os.environ.get("FP32_DIR", "fp32")
OUT = HERE / "models"

NAMES = {
    "encoder_model.onnx": f"stt-whisper-{SIZE}-en-encoder-int8.onnx",
    "decoder_model.onnx": f"stt-whisper-{SIZE}-en-decoder-int8.onnx",
}
VOCAB = f"stt-whisper-{SIZE}-en-vocab.json"


def quantize(src: Path, dst: Path) -> None:
    # Transformer only: quantize MatMul/Gemm, leave everything else alone.
    quantize_dynamic(
        str(src),
        str(dst),
        weight_type=QuantType.QInt8,
        op_types_to_quantize=["MatMul", "Gemm"],
        per_channel=True,
    )


def fetch_vocab() -> None:
    url = f"https://huggingface.co/{MODEL_ID}/resolve/main/vocab.json"
    dst = OUT / VOCAB
    urllib.request.urlretrieve(url, dst)
    vocab = json.loads(dst.read_text(encoding="utf-8"))
    print(f"vocab ok: {len(vocab)} tokens, {LICENSE}")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for src_name, dst_name in NAMES.items():
        src = FP32 / src_name
        assert src.exists(), f"missing {src}, run optimum-cli export first"
        quantize(src, OUT / dst_name)
        print(f"wrote {dst_name}")
    fetch_vocab()
    print(f"Copy the {SIZE}-int8.onnx and {VOCAB} files into Content/Assets/Models/.")


if __name__ == "__main__":
    main()
