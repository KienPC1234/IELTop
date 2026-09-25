# IELTop

An offline IELTS practice app for Windows. Built with WPF and .NET 10.

Most of it works with no internet and no model files: vocabulary, reading,
listening, writing tasks, and a timer based mock test. Optional AI features
(essay feedback, word explanations, speaking tips) need a language model,
which can be a local server or an online API.

## What it does

- Vocabulary: save words with meaning, example and topic. Ask a model to explain a word.
- Reading: read a passage and answer questions. Checking happens locally.
- Listening: play a clip, answer questions, reveal the transcript.
- Writing: write an essay and get structured feedback with an estimated band.
- Speaking: read a sentence, record it, and see which sounds were off.
- Mock Test: timed parts in one screen, submit at the end, then review answers.
- Content: import txt, md, json, csv, pdf and docx files, or export papers to share.
- Models: list of the offline models the app can use, plus a Load button each.
- Settings: connect any OpenAI compatible chat server.

Practice bands are estimates for study only. They are not official IELTS scores.

## Requirements

- Windows 10 or 11
- .NET 10 desktop runtime
- A microphone for the Speaking section (optional)

## Build

```powershell
cd IELTop
dotnet build --nologo
dotnet run
```

## Content and models

The Content page imports study files and exports papers. Import reads plain
text out of txt, md, json, csv, pdf and docx. Scanned pages are not read,
because image reading is not included.

With a language model, import also drafts the title and the questions, which
you then check and edit. Without one, import saves the text into the right
section and you fill in the questions yourself. Either way nothing is blocked.

Export writes JSON files into a fresh folder, so you can share one paper, every
paper, or all practice content at once.

Study content is plain JSON. You can also add it by hand:

- `IELTop/Assets/Reading/*.json` passages
- `IELTop/Assets/Listening/*.json` items, audio in `IELTop/Assets/Audio`
- `IELTop/Assets/Writing/*.json` tasks
- `IELTop/Assets/Exams/*.json` mock papers

Each folder has a short README with the exact format.

Offline models go in `IELTop/Assets/Models`. The list of models the app uses,
with source and license, is in `IELTop/Assets/Models/README.md`. Missing files
are fine. The app tells you which file is missing and keeps working.

## Language model (optional)

Open Settings and enter:

- Base URL, for example `http://localhost:11434/v1` for Ollama
- Model name, for example `llama3.1`
- API key, only if the server needs one

Press Check to validate the settings, then Test connection to confirm the server
answers. The key is encrypted on your computer. Without a model, these stay
disabled: Writing feedback, word explanations, speaking tips, and the AI part of
import. Reading, Listening, Vocabulary and Mock Test always work offline. If a
model is set but the server is down, the app falls back to the offline path
instead of failing.

Turn on the vision option only for a model that reads images, such as gpt-4o or
llava. It lets Content import a photo of a page. With a text only model, leave it
off.

## Building the pronunciation model

The Speaking scores need a small phoneme model built from a script:

```powershell
cd tools/speaking-mdd
conda env create -p .venv -f environment.yml
conda activate .\.venv
python export_onnx.py --out ./models
python check_phoneme_map.py
```

Copy `mdd-wav2vec2-base-int8.onnx` and `mdd-labels.json` into
`IELTop/Assets/Models/`. The full steps are in that folder's README.

## Project layout

```
IELTop/
  Assets/        study content and models
  Data/          local database
  Models/        data types
  Services/      AI, audio, storage
  ViewModels/    screen logic
  Views, MainWindow.xaml
tools/           Python helpers, each with its own conda environment
```

## License

See the repository license. Model files keep their own licenses, listed in
`IELTop/Assets/Models/README.md`.
