# Mock test papers

The Mock Test section reads every `.json` file in this folder. Add content here
without touching code.

## File format

```json
{
  "title": "Paper title",
  "source": "Where the content comes from and its license",
  "parts": [
    {
      "id": "R1",
      "skill": "Reading",
      "title": "Reading Passage 1",
      "minutes": 12,
      "instructions": "Short instruction shown above the questions.",
      "material": "The passage or transcript text.",
      "questions": [
        {
          "number": 1,
          "prompt": "The question text",
          "options": [
            { "key": "A", "text": "First choice" },
            { "key": "B", "text": "Second choice" }
          ],
          "correctKey": "B"
        }
      ]
    }
  ]
}
```

Notes:

- `skill` is one of Listening, Reading, Writing.
- A part with no `questions` is shown as a writing task with a prompt only.
- `minutes` drives the countdown timer for that part.
- The band estimate is a rough guide for multiple choice practice. It is not an official IELTS score.

## Content rules

- Only use source material you have the right to redistribute. Record the source in the `source` field.
- `sample-test-1.json` is original content written for IELTop and is safe to keep.
