# Audio files

Put listening audio here. File names are referenced by `audioFile` in
`Assets/Listening/*.json`.

- Supported formats: WAV, MP3, M4A (whatever the local player supports).
- WAV files work best. Speaking captures use 16 kHz mono.
- Audio binaries are ignored by git. Ship them separately or let users add them.
- If a referenced file is missing, the app disables the Play button and names
  the missing file instead of failing silently.
