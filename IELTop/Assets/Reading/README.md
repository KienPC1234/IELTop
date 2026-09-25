# Reading content

Every `.json` file here is one reading passage. The Reading section lists them
automatically, so adding content needs no code change.

## Format

```json
{
  "id": "R-UNIQUE-ID",
  "title": "Passage title",
  "source": "Where the text comes from and its license",
  "level": "Intermediate",
  "body": "The passage text.",
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

- `body` supports plain text. Use `\n\n` for paragraph breaks.
- Matching is local, so the section works with no model and no internet.
- Only use text you have the right to share, and record the source.
