# Speech to text (whisper-tiny.en)

Offline transcription for Speaking answers. MIT licensed
`openai/whisper-tiny.en`, exported with Optimum, quantized to INT8.

## Build

```powershell
conda env create -p .venv -f environment.yml
conda activate .\.venv
optimum-cli export onnx --model openai/whisper-tiny.en --task automatic-speech-recognition --output ./models/fp32
python export_onnx.py
```

Copy into `Content/Assets/Models/`:

- `stt-whisper-tiny-en-encoder-int8.onnx`
- `stt-whisper-tiny-en-decoder-int8.onnx`
- `stt-whisper-tiny-en-vocab.json`

## Notes

- English only, tiny size, runs on CPU. A 60 second answer takes a few
  seconds per 30 second window.
- Only MatMul and Gemm are quantized. The encoder convolutions stay in
  full precision because onnxruntime cannot quantize them.
- The app decodes greedily with the prompt
  SOT, English, Transcribe, NoTimestamps and skips end of text on the
  first step, matching the reference decoder.
- Without these files the app still works: speaking answers fall back to
  a typed transcript.
