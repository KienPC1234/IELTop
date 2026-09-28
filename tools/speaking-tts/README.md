# Examiner voice (Piper + espeak-ng)

Neural text to speech for Speaking cues and Listening transcripts.
MIT licensed `en_US-lessac-medium` voice from `rhasspy/piper-voices`,
plus a portable espeak-ng 1.52.0 for phonemes.

## Fetch

```powershell
conda env create -p .venv -f environment.yml
conda activate .\.venv
python fetch_models.py
```

Copy into `IELTop/Assets/Models/`:

- `tts-piper-lessac-medium.onnx`
- `tts-piper-lessac-medium.onnx.json`
- folder `espeak-ng/` with `espeak-ng.exe` and `espeak-ng-data/`

The script also runs a test synthesis and refuses to finish when the
voice produces almost no audio.

## Notes

- Voice and phonemizer files are ignored by git. Only this script and
  README are committed.
- The app prefers the neural voice and falls back to the built-in
  Windows voice when files are missing, so the app still talks offline.
- The voice reads each transcript clause once through espeak-ng
  (`-v en-us -q --ipa=2 -x`), maps phonemes to ids from the voice
  config, and runs the VITS model at 22050 Hz.
