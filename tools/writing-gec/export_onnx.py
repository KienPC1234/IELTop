"""Export gec-t5_small to ONNX for deterministic grammar checking.

Runs once on a dev machine, not in the app:
    optimum-cli export onnx --model Unbabel/gec-t5_small \
        --task text2text-generation --output ./models/fp32
    python export_onnx.py

Copies gec-t5-small-encoder-int8.onnx, gec-t5-small-decoder-int8.onnx,
and gec-t5-spiece.model into IELTop/Assets/Models/.
"""

import urllib.request
from pathlib import Path

from onnxruntime.quantization import QuantType, quantize_dynamic

MODEL_ID = "Unbabel/gec-t5_small"
LICENSE = "Apache-2.0"
HERE = Path(__file__).resolve().parent
FP32 = HERE / "models" / "fp32"
OUT = HERE / "models"

NAMES = {
    "encoder_model.onnx": "gec-t5-small-encoder-int8.onnx",
    "decoder_model.onnx": "gec-t5-small-decoder-int8.onnx",
}


def quantize(src: Path, dst: Path) -> None:
    # Pure transformer: quantize MatMul/Gemm only.
    quantize_dynamic(
        str(src),
        str(dst),
        weight_type=QuantType.QInt8,
        op_types_to_quantize=["MatMul", "Gemm"],
        per_channel=True,
    )


def fetch_spiece() -> None:
    url = f"https://huggingface.co/{MODEL_ID}/resolve/main/spiece.model"
    dst = OUT / "gec-t5-spiece.model"
    urllib.request.urlretrieve(url, dst)
    print(f"spiece ok: {dst.stat().st_size} bytes, {LICENSE}")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for src_name, dst_name in NAMES.items():
        src = FP32 / src_name
        assert src.exists(), f"missing {src}, run optimum-cli export first"
        quantize(src, OUT / dst_name)
        print(f"wrote {dst_name}")
    fetch_spiece()
    print("Copy the *-int8.onnx and gec-t5-spiece.model files into IELTop/Assets/Models/.")


if __name__ == "__main__":
    main()
