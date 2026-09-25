# Writing tasks

Every `.json` file here is one writing task. The Writing section lists tasks
automatically and shows the prompt and word target.

## Format

```json
{
  "id": "W-UNIQUE-ID",
  "title": "Task title",
  "source": "Where the task comes from and its license",
  "taskType": "Task 2",
  "minutes": 40,
  "minimumWords": 250,
  "prompt": "The essay question students must answer.",
  "tips": [
    "One short tip",
    "Another tip"
  ]
}
```

Notes:

- `minutes` and `minimumWords` guide the student. The AI review uses the prompt.
- With a language model configured, the Get AI feedback button returns an
  estimated band and structured feedback. Without one, the button is disabled
  and explains why.
- Estimated bands are practice guidance only, never official IELTS scores.
- Only use task text you have the right to share, and record the source.
