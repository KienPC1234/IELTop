# Grammar check (gec-t5_small)

Deterministic grammar correction for Writing. Apache-2.0
`Unbabel/gec-t5_small` (T5-small, F0.5 60.70), exported with Optimum,
quantized to INT8.

## Build

```powershell
conda env create -p .venv -f environment.yml
conda activate .\.venv
optimum-cli export onnx --model Unbabel/gec-t5_small --task text2text-generation --output ./models/fp32
python export_onnx.py
```

Copy into `Content/Assets/Models/`:

- `gec-t5-small-encoder-int8.onnx`
- `gec-t5-small-decoder-int8.onnx`
- `gec-t5-spiece.model`

## Notes

- The app corrects each sentence with the `gec: ` prefix and counts
  errors per 100 words. The Writing grammar band is capped from that
  count, so generous AI marking cannot hide real errors.
- Only MatMul and Gemm are quantized.
- Without these files the app still works: Writing falls back to AI
  marking alone.
