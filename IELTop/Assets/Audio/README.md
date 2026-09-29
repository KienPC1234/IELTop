# Audio files

Put listening audio here. File names are referenced by `audioFile` in
`Assets/Exams/*.json`.

- Supported formats: WAV, MP3, M4A, Opus (decoded through Windows Media Foundation).
- WAV files work best.
- Audio binaries are ignored by git. Ship them separately or let users add them.
- If a part ships no audio file, the app shows the transcript to read and
  never fakes the clip with a synthetic voice. Listening uses real exam audio only.
