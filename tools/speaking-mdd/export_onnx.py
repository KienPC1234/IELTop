"""Xuat checkpoint wav2vec2 CTC sang ONNX INT8 cho chuc nang Luyen noi (MDD).

Pipeline day du: G2P (chuan bi cau mau) -> model du doan don vi am
-> gióng hang Levenshtein de phat hien thay the / bo sot / them am.
File nay chi lam buoc xuat model; logic so khop nam trong app C#
(IELTop/Services/Ai/MddPhonemeService.cs) va doc mdd-labels.json
de luon khop bo tu vung voi checkpoint duoc chon.

Cach dung (moi truong conda .venv, xem environment.yml):
    conda env create -p .venv -f environment.yml
    conda activate .\\.venv
    python export_onnx.py --out ./models

Mac dinh dung checkpoint CTC cap am vi:
    bobboyms/wav2vec2-base-en-phoneme-ctc-41h  (license Apache-2.0)
Do la ban wav2vec2-base fine-tune de nhan chuoi am vi tieng Anh, phu hop
hon ban character-level nhu facebook/wav2vec2-base-960h cho bai toan MDD.
Script tu doc bo tu vung tu tokenizer nen app C# van giai ma dung
ma khong can sua code. Doi checkpoint khac bang --model neu can.

Ket qua trong ./models:
    mdd-wav2vec2-base-int8.onnx  (chep vao Content/Assets/Models/)
    mdd-labels.json              (chep vao Content/Assets/Models/)
"""

from __future__ import annotations

import argparse
import json
import shutil
import time
from pathlib import Path

import numpy as np


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Export wav2vec2 CTC to ONNX INT8 for MDD.")
    p.add_argument("--model", default="bobboyms/wav2vec2-base-en-phoneme-ctc-41h",
                   help="HuggingFace checkpoint (mac dinh la ban CTC cap am vi).")
    p.add_argument("--out", default="./models", help="Thu muc ket qua.")
    p.add_argument("--opset", type=int, default=14, help="ONNX opset.")
    p.add_argument("--no-quantize", action="store_true",
                   help="Bo qua INT8, giu ban FP32 (nang hon, ton RAM).")
    p.add_argument("--verify-seconds", type=float, default=3.0,
                   help="Do dai audio gia de do thu (giay).")
    return p.parse_args()


def export_fp32(model_id: str, fp32_dir: Path, opset: int):
    from optimum.onnxruntime import ORTModelForCTC
    from transformers import AutoProcessor

    print(f"[1/4] Tai va xuat {model_id} sang ONNX (FP32)...", flush=True)
    model = ORTModelForCTC.from_pretrained(model_id, export=True)
    processor = AutoProcessor.from_pretrained(model_id)
    model.save_pretrained(fp32_dir)
    processor.save_pretrained(fp32_dir)
    return fp32_dir / "model.onnx", processor


def save_labels(processor, out_dir: Path) -> list[str]:
    vocab = processor.tokenizer.get_vocab()  # token -> id
    labels = [tok for tok, _ in sorted(vocab.items(), key=lambda kv: kv[1])]
    path = out_dir / "mdd-labels.json"
    path.write_text(json.dumps(labels, ensure_ascii=False), encoding="utf-8")
    print(f"[2/4] Ghi {path} ({len(labels)} don vi).", flush=True)
    return labels


def report_labels(labels: list[str], out_dir: Path) -> None:
    """
    In bo nhan ra man hinh de nguoi dung doi chieu voi phoneme-map.json.
    Model am vi xuat IPA (vi du θ, ɪ, ŋ), khong phai ARPABET (TH, IH, NG),
    nen hai ben phai cung mot he ki hieu moi so sanh duoc.
    """
    special = {"<pad>", "<unk>", "<s>", "</s>"}
    usable = [t for t in labels if t not in special and t != "|"]

    report = out_dir / "mdd-labels.txt"
    report.write_text("\n".join(usable), encoding="utf-8")
    print(f"  {len(usable)} nhan dung duoc ghi kem o {report.name}.", flush=True)
    print("  Luu y: nhan la IPA. phoneme-map.json phai dung cung IPA, "
          "neu khong diem se luon sai.", flush=True)


def quantize_int8(fp32_path: Path, final_path: Path, skip: bool) -> Path:
    if skip:
        shutil.copyfile(fp32_path, final_path)
        print("[3/4] Bo qua luong tu hoa, giu ban FP32.", flush=True)
        return final_path
    from onnxruntime.quantization import QuantType, quantize_dynamic

    print("[3/4] Luong tu hoa dong sang INT8 (giam ~3x dung luong)...", flush=True)
    # wav2vec2 co Conv trong pos_conv_embed ma onnxruntime khong luong tu hoa duoc
    # (trong so chua duoc fold thanh initializer). Chi luong tu hoa MatMul/Gemm,
    # phan chiem gan het trong so, tranh loi "Expected ... to be an initializer".
    quantize_dynamic(
        str(fp32_path),
        str(final_path),
        weight_type=QuantType.QInt8,
        op_types_to_quantize=["MatMul", "Gemm"],
    )
    return final_path


def verify(final_path: Path, seconds: float) -> None:
    import onnxruntime as ort

    print("[4/4] Kiem tra chay that voi audio gia 16kHz mono...", flush=True)
    opts = ort.SessionOptions()
    opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    session = ort.InferenceSession(str(final_path), sess_options=opts,
                                   providers=["CPUExecutionProvider"])
    name = session.get_inputs()[0].name
    dummy = (np.random.randn(1, int(16000 * seconds)).astype(np.float32)) * 0.01
    started = time.perf_counter()
    out = session.run(None, {name: dummy})
    elapsed = time.perf_counter() - started
    print(f"  Input {dummy.shape} -> output {np.asarray(out[0]).shape} "
          f"trong {elapsed:.2f}s cho {seconds:.1f}s audio.", flush=True)


def main() -> None:
    args = parse_args()
    out = Path(args.out)
    fp32_dir = out / "fp32"
    fp32_dir.mkdir(parents=True, exist_ok=True)

    fp32_path, processor = export_fp32(args.model, fp32_dir, args.opset)
    labels = save_labels(processor, out)
    report_labels(labels, out)
    final_path = quantize_int8(fp32_path, out / "mdd-wav2vec2-base-int8.onnx",
                               args.no_quantize)
    verify(final_path, args.verify_seconds)

    fp32_mb = fp32_path.stat().st_size / 1e6
    final_mb = final_path.stat().st_size / 1e6
    print(f"Xong: FP32 {fp32_mb:.0f}MB -> INT8 {final_mb:.0f}MB.", flush=True)
    print("Chep 2 file mdd-wav2vec2-base-int8.onnx va mdd-labels.json "
          "vao Content/Assets/Models/ de app nhan model.", flush=True)


if __name__ == "__main__":
    main()
