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
      "topic": "Cities and nature",
      "taskType": "Passage 1",
      "minutes": 12,
      "instructions": "Short instruction shown above the questions.",
      "material": "The passage or transcript text.",
      "questions": [
        {
          "number": 1,
          "kind": "choice",
          "prompt": "The question text",
          "options": [
            { "key": "A", "text": "First choice" },
            { "key": "B", "text": "Second choice" }
          ],
          "correctKey": "B",
          "explanation": "Why B is right, shown in Reading review."
        },
        {
          "number": 2,
          "kind": "gap",
          "prompt": "The library opens at ___ on weekdays.",
          "options": [],
          "correctKey": "",
          "gapAnswer": "8:00|8|8am"
        }
      ]
    }
  ]
}
```

Notes:

- `skill` is one of Listening, Reading, Writing, Speaking.
- `kind` is `choice` (default), `gap`, or `match`. Choice uses `options` plus
  `correctKey`, and also covers TRUE/FALSE/NOT GIVEN by putting those
  words in the options. Gap asks the student to type, checked case
  insensitively against `gapAnswer`, where `|` separates accepted
  alternatives. Match shows a bank of items dragged into one gap per
  row, using `bank` plus `matchRows` with `label` and `answer` each.
- `topic` and `taskType` feed the type filter, for example Task 2
  Opinion, Part 2 Cue Card, or Passage 1.
- A part with no `questions` is a Writing or Speaking part with a
  prompt only. Listening parts read `material` aloud when no
  `audioFile` is shipped.
- `minutes` drives the countdown timer for that part.
- The band estimate is a rough guide for practice. It is not an official IELTS score.

## Content rules

- Only use source material you have the right to redistribute. Record the source in the `source` field.
- `sample-test-1.json` is original content written for IELTop and is safe to keep.
