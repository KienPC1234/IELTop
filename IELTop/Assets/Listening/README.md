# Listening content

Every `.json` file here is one listening item. Audio files live in
`Assets/Audio/`. The Listening section lists items automatically.

## Format

```json
{
  "id": "L-UNIQUE-ID",
  "title": "Item title",
  "source": "Where the audio and text come from and their license",
  "level": "Intermediate",
  "audioFile": "my-clip.wav",
  "transcript": "Full transcript text.",
  "questions": [
    {
      "number": 1,
      "prompt": "Question text",
      "options": [
        { "key": "A", "text": "First choice" },
        { "key": "B", "text": "Second choice" }
      ],
      "correctKey": "B"
    }
  ]
}
```

Notes:

- `audioFile` is just the file name. Put the file in `Assets/Audio/`.
- If the audio file is missing, the Play button is disabled and the UI names
  the missing file. The app never crashes over a missing clip.
- Audio files are ignored by git, so ship them separately or let users add them.
- Only use audio you have the right to share, and record the source.
