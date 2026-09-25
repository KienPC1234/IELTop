# Models

Drop `.onnx` files here. The app finds them through
`Services/Ai/OnnxModelRegistry.cs`, which is the single source of truth for the
list below. Large files are not committed to git.

Every entry is a real, downloadable model. All run on CPU.

| Slot | Files | Skill | Source | License |
|------|-------|-------|--------|---------|
| vocab-embeddings | `all-MiniLM-L6-v2.onnx` + `vocab.txt` | Vocabulary | Qdrant/all-MiniLM-L6-v2-onnx | Apache-2.0 |
| mdd-wav2vec2-base | `mdd-wav2vec2-base-int8.onnx` + `mdd-labels.json` | Speaking | bobboyms/wav2vec2-base-en-phoneme-ctc-41h, exported by `tools/speaking-mdd` | Apache-2.0 |
| whisper-tiny-encoder | `whisper-tiny-encoder.onnx` + `whisper-tiny-tokens.txt` | Speaking | openai/whisper-tiny, exported with Optimum | MIT |
| whisper-tiny-decoder | `whisper-tiny-decoder.onnx` + `whisper-tiny-tokens.txt` | Speaking | openai/whisper-tiny, exported with Optimum | MIT |
| listening-vad | `silero-vad.onnx` | Listening | onnx-community/silero-vad | MIT |
| grammar-gec | `grammar-gec-t5-small.onnx` + `spiece.model` | Writing | vennify/t5-base-grammar-correction, exported with Optimum | CC-BY-NC-SA-4.0 |

## Notes

- `grammar-gec` is licensed for non-commercial use. Remove it or replace it if
  you plan to sell the app.
- The pronunciation model is the one that makes the Speaking section score
  sounds. Build it with the steps in `tools/speaking-mdd/README.md`.
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
