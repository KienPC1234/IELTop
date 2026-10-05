# Models

Drop `.onnx` files here. The app finds them through
`Services/Ai/OnnxModelRegistry.cs`, which is the single source of truth for the
list below. Large files are not committed to git.

Every entry is a real, downloadable model. All run on CPU.

| Slot | Files | Skill | Source | License |
|------|-------|-------|--------|---------|
| mdd-wav2vec2-base | `mdd-wav2vec2-base-int8.onnx` + `mdd-labels.json` | Speaking | bobboyms/wav2vec2-base-en-phoneme-ctc-41h, exported by `tools/speaking-mdd` | Apache-2.0 |
| stt-whisper-base-en | `stt-whisper-base-en-encoder-int8.onnx` + `stt-whisper-base-en-decoder-int8.onnx` + `stt-whisper-base-en-vocab.json` | Speaking | openai/whisper-base.en, exported by `tools/speech-stt base` | MIT |
| stt-whisper-small-en | `stt-whisper-small-en-encoder-int8.onnx` + `stt-whisper-small-en-decoder-int8.onnx` + `stt-whisper-small-en-vocab.json` | Speaking | openai/whisper-small.en, exported by `tools/speech-stt small` | MIT |
| gec-t5-small | `gec-t5-small-encoder-int8.onnx` + `gec-t5-small-decoder-int8.onnx` + `gec-t5-spiece.model` | Writing | Unbabel/gec-t5_small, exported by `tools/writing-gec` | Apache-2.0 |
| tts-piper-lessac | `tts-piper-lessac-medium.onnx` + `tts-piper-lessac-medium.onnx.json` + folder `espeak-ng/` (with `espeak-ng.exe` and `libespeak-ng.dll`) | Speaking, Listening | rhasspy/piper-voices en_US-lessac-medium, fetched by `tools/speaking-tts` | MIT |

## Notes

- The pronunciation model is the one that makes the Speaking section score
  sounds. Build it with the steps in `tools/speaking-mdd/README.md`. The app
  scores a recording against `phoneme-map.json` and shows the words to fix.
- The transcription model turns Speaking recordings into text for AI
  marking. Build it with `tools/speech-stt/README.md`. Without it,
  speaking answers fall back to a typed transcript. tiny.en was dropped for
  accuracy: the app now uses small.en when installed, else base.en. Measured on
  one synthetic clip ("the weather is nice today"): tiny gave "[Music]" or wrong
  words, base gave "The Where's Nice Today", small gave "But where's Nastudy?".
  All three miss the start on fast synthetic speech, so measure again on real
  human recordings before trusting a transcript blindly.
- The grammar model corrects Writing sentence by sentence and caps the
  grammar band from measured errors. Build it with
  `tools/writing-gec/README.md`. Without it, Writing falls back to AI
  marking alone.
- The examiner voice, `tools/speaking-tts` (Piper), is used by the model server
  (`IELTop.Desktop.exe --model-server`) and by read-aloud. The offline Piper voice
  model and its espeak-ng folder are needed for that neural voice.
- `phoneme-map.json` (committed) is a full word to IPA table (about 125,000
  words) used to build the target pronunciation without a G2P library. It is
  generated from CMUdict (BSD-2-Clause) by
  `python tools/speaking-mdd/build_phoneme_map.py`, which maps ARPABET to the
  exact IPA symbol set the model emits. The model emits IPA, so this map must
  use IPA too. After exporting a model, run
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
