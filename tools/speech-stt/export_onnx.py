"""Export whisper-tiny.en to ONNX for offline transcription.

Runs once on a dev machine, not in the app:
    optimum-cli export onnx --model openai/whisper-tiny.en \
        --task automatic-speech-recognition --output ./models/fp32
    python export_onnx.py

Copies stt-whisper-tiny-en-encoder-int8.onnx,
stt-whisper-tiny-en-decoder-int8.onnx, and stt-whisper-tiny-en-vocab.json
into IELTop/Assets/Models/.
"""

import json
import shutil
import urllib.request
from pathlib import Path

from onnxruntime.quantization import QuantType, quantize_dynamic

MODEL_ID = "openai/whisper-tiny.en"
LICENSE = "MIT"
HERE = Path(__file__).resolve().parent
FP32 = HERE / "models" / "fp32"
OUT = HERE / "models"

NAMES = {
    "encoder_model.onnx": "stt-whisper-tiny-en-encoder-int8.onnx",
    "decoder_model.onnx": "stt-whisper-tiny-en-decoder-int8.onnx",
}


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
    for name in ("vocab.json",):
        url = f"https://huggingface.co/{MODEL_ID}/resolve/main/{name}"
        dst = OUT / "stt-whisper-tiny-en-vocab.json"
        urllib.request.urlretrieve(url, dst)
        vocab = json.loads(dst.read_text(encoding="utf-8"))
        assert len(vocab) == 50257, f"unexpected vocab size {len(vocab)}"
        print(f"vocab ok: {len(vocab)} tokens, {LICENSE}")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for src_name, dst_name in NAMES.items():
        src = FP32 / src_name
        assert src.exists(), f"missing {src}, run optimum-cli export first"
        quantize(src, OUT / dst_name)
        print(f"wrote {dst_name}")
    fetch_vocab()
    print("Copy the *-int8.onnx and *-vocab.json files into IELTop/Assets/Models/.")


if __name__ == "__main__":
    main()
