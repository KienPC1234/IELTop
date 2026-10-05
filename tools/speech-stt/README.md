# Speech to text (whisper .en)

Offline transcription for Speaking answers. MIT licensed
`openai/whisper-<size>.en`, exported with Optimum, quantized to INT8.

## Build

Pick a size. `small` is more accurate and slower; `base` is a good balance.
(tiny was dropped: too weak.) The app uses the largest size that is installed,
so you can add more than one.

```powershell
python -m venv .venv
.\.venv\Scripts\python -m pip install "optimum[onnxruntime]" onnxruntime torch
.\.venv\Scripts\optimum-cli export onnx --model openai/whisper-base.en --task automatic-speech-recognition --output ./models/fp32
.\.venv\Scripts\python export_onnx.py base
```

Copy into `Content/Assets/Models/`:

- `stt-whisper-base-en-encoder-int8.onnx`
- `stt-whisper-base-en-decoder-int8.onnx`
- `stt-whisper-base-en-vocab.json`

Repeat with the other size to add it; they sit side by side.

## Notes

- English only. Runs on CPU, and on the GPU through DirectML when the PC has one.
  A 60 second answer takes a few seconds per 30 second window.
- Only MatMul and Gemm are quantized. The encoder convolutions stay in
  full precision because onnxruntime cannot quantize them.
- The app decodes greedily with the prompt
  SOT, English, Transcribe, NoTimestamps and skips end of text on the
  first step, matching the reference decoder.
- `OnnxModelRegistry.ResolveSttSlot()` picks the largest installed size, so a
  bigger model is used the moment its files are present. No code change needed.
- Without these files the app still works: speaking answers fall back to a
  typed transcript.
