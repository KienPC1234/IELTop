# Models

Drop `.onnx` files here. The app finds them through
`Services/Ai/OnnxModelRegistry.cs`, which is the single source of truth for the
list below. Large files are not committed to git.

Every entry is a real, downloadable model. All run on CPU.

| Slot | Files | Skill | Source | License |
|------|-------|-------|--------|---------|
| mdd-wav2vec2-base | `mdd-wav2vec2-base-int8.onnx` + `mdd-labels.json` | Speaking | bobboyms/wav2vec2-base-en-phoneme-ctc-41h, exported by `tools/speaking-mdd` | Apache-2.0 |
| stt-whisper-tiny-en | `stt-whisper-tiny-en-encoder-int8.onnx` + `stt-whisper-tiny-en-decoder-int8.onnx` + `stt-whisper-tiny-en-vocab.json` | Speaking | openai/whisper-tiny.en, exported by `tools/speech-stt` | MIT |
| gec-t5-small | `gec-t5-small-encoder-int8.onnx` + `gec-t5-small-decoder-int8.onnx` + `gec-t5-spiece.model` | Writing | Unbabel/gec-t5_small, exported by `tools/writing-gec` | Apache-2.0 |
| tts-piper-lessac | `tts-piper-lessac-medium.onnx` + `tts-piper-lessac-medium.onnx.json` + folder `espeak-ng/` | Speaking, Listening | rhasspy/piper-voices en_US-lessac-medium, fetched by `tools/speaking-tts` | MIT |

## Notes

- The pronunciation model is the one that makes the Speaking section score
  sounds. Build it with the steps in `tools/speaking-mdd/README.md`.
- The transcription model turns Speaking recordings into text for AI
  marking. Build it with `tools/speech-stt/README.md`. Without it,
  speaking answers fall back to a typed transcript.
- The grammar model corrects Writing sentence by sentence and caps the
  grammar band from measured errors. Build it with
  `tools/writing-gec/README.md`. Without it, Writing falls back to AI
  marking alone.
- The examiner voice reads Speaking cues and Listening transcripts.
  Fetch it with `tools/speaking-tts/README.md`. It needs the portable
  `espeak-ng/` folder next to it for phonemes. Without it, the built-in
  Windows voice reads instead.
- `phoneme-map.json` (committed) is a word to IPA table used to build the target
  pronunciation without a full G2P library. The model emits IPA, so this map
  must use IPA too. After exporting a model, run
  `python tools/speaking-mdd/check_phoneme_map.py` to confirm they agree.
- The exported pronunciation model is about 122 MB at INT8 and scores a 3 second
  clip in roughly 0.12 s on a normal CPU.
- To add a model, add one `OnnxModelSlot` line in `OnnxModelRegistry.cs`. The
  Models page updates on its own.

## Downloading

For the pronunciation model, use `tools/speaking-mdd/export_onnx.py`. It exports
to ONNX, quantizes to INT8, writes the labels, and runs a test inference.

For other HuggingFace models, use Optimum to export to ONNX, for example:

```powershell
optimum-cli export onnx --model <model-id> <output-folder>
```

Then rename the file to match the slot and copy it here.

## Rules

- `.onnx` files are ignored by `.gitignore`. Keep `README.md`, `.gitkeep` and
  `phoneme-map.json`.
- The project copies everything under `Assets/Models` to the output folder.
