# IELTop

A mock test only IELTS app. Built with .NET 10.

One core, one cross platform client:

- `IELTop.Desktop` is the app. A native Photino window hosts a React web UI,
  and the web UI calls the offline services over a small local bridge. No
  Electron, no Node at run time, nothing online. It runs on Windows, macOS,
  and Linux.
- `IELTop.Core` holds everything shared: models, SQLite, exam storage, AI,
  scoring, and the exam engine. The web UI draws; Core does the work, so the
  ONNX models and the banding logic are written once.
- `Content` holds the shared read only content: exam papers, audio, images,
  and the optional model files.

There are no practice sections. You sit a full test or one skill
(Reading, Writing, or Speaking), get a band range, and get detailed AI
feedback. Listening only runs inside a full test, because its audio
cannot be split into parts.

## What it does

- Mock Test: IDP style screen with a top timer bar, part tabs, and a
  volume control. Listening clips play once. Speaking runs without pause.
  Reading uses a split screen with the passage on the left.
- Bands as ranges: every score shows a range such as 6.0 to 7.0, because
  examiners vary. Three marking levels: Lenient, Standard, Strict.
- Results: past tests with band ranges plus the official Writing and
  Speaking criteria tables.
- Review: Listening names only the wrong questions and shows no answers.
  Reading shows every answer with an explanation. Writing and Speaking
  are marked by a language model when one is set.
- Strict mode: voluntary full screen exam lock that blocks risky keys.
- Settings: connect any OpenAI compatible chat server.

Bands are estimates for practice only. They are not official IELTS scores.

## The four skills in this app

- Listening: 4 parts in a full test. The clip plays once and never
  replays, like the real test. If a paper ships no audio file, the
  transcript is shown to read instead of a fake voice. Review lists wrong
  questions only, with no answers and no AI feedback.
- Reading: passages with a split screen. Can run alone or in a full
  test. Review shows every answer with an explanation, and AI explains
  wrong answers when a model is set.
- Writing: Task 1 and Task 2 as separate parts with live word counts.
  Marked by AI after submit, on the four official criteria.
- Speaking: Part 1, Part 2 cue card, and Part 3 as separate parts with
  fixed recording times and no pause, like a real speaking session.
  Type what you said so AI marking can work. The real IDP speaking test
  is face to face with an examiner; this app simulates that flow.

## Requirements

- .NET 10 desktop runtime
- WebView2 runtime on Windows (already present on Windows 10 and 11),
  WebKitGTK on Linux, WKWebView on macOS
- Node.js 20.19 or newer to build the web UI
- A microphone for the Speaking section
- Speakers or headphones for the Listening section

## Build

`dotnet build` builds the web UI first, then the host, so one command brings
everything up to date:

```powershell
cd IELTop.Desktop
dotnet build
dotnet run
```

For a live UI while editing, run the Vite dev server in one terminal and point
the host at it. See `IELTop.Desktop/README.md` if one is present.

The web UI is a static bundle of about 280 KB. The desktop app serves it from a
loopback port inside the host process, so no server is installed and nothing
leaves the machine.

## Package and release

Publishes the app and zips it for a GitHub Release:

```powershell
pwsh tools/pack-release.ps1 -Version 1.0.1
pwsh tools/pack-release.ps1 -Version 1.0.1 -Runtime linux-x64
```

Output lands in `releases/` as `IELTop-<version>-<runtime>.zip`. Attach it to a
GitHub Release tagged `v<version>`; the app checks GitHub Releases for a newer
version and opens the release page to install, which works the same on every OS.

The logo and icon come from `tools/make-icon/make_icon.py`, a Python script
that reads `Content/Assets/Images/IELTop-rounded.png`:

```powershell
conda env create -f tools/make-icon/environment.yml
conda run -n ieltop-icon python tools/make-icon/make_icon.py
```

It writes `app.ico`, `logo-256.png`, and `logo-64.png` into
`Content/Assets/Images`. `app.ico` is wired into the build and used by the
installer.

## Test papers and models
Mock papers are plain JSON in `Content/Assets/Exams/`. Each part has a
skill, minutes, material, and questions. Listening parts read their
`material` aloud when no `audioFile` is shipped. Reading questions can
carry an `explanation` shown in review. Papers carry a category, a
level band, and tags for grouping in the Library.

The Library page browses every paper with search, category, and skill
filters. Drag papers into the basket (or press +) to assemble one
custom mixed test, then start it. Downloaded papers can be deleted
from the Mock Test setup under Your papers.

Import paper JSON files with the Import button. Bad files are skipped
with a reason, and name clashes get a numbered file instead of an
overwrite. Export shown sends the filtered list to a timestamped
folder under Documents with audio clips included. The AI check button
asks the language model to rate one paper when it is configured.

## Content servers

The Servers page downloads mock test papers from IELTop content
servers (protocol `ieltop/1`), community or private, over plain HTTP.
The community list ships in `Content/servers.txt` (`Name | BaseUrl` per
line). Users can add their own servers, which are kept on their
computer.

A full server with an admin portal and a contributor submission flow
lives in `IELTop_Content_Server/` (ASP.NET Core, SQLite or Postgres,
optional Redis, Cloudflare Turnstile, SMTP notifications, and an
OpenAI compatible review step). See `IELTop_Content_Server/README.md`.

Servers can be anonymous, need an access code, or need a username and
password. Codes and passwords stay encrypted on the computer. Saved
papers land in `%LocalAppData%/IELTop/content/Exams` with audio in
`%LocalAppData%/IELTop/content/Audio`, so they show up in Mock Test
right away.

Papers carry a category (for example Academic), a level band, skills,
and an update date. The Servers page searches by title and filters by
category, marks downloaded papers, and flags a paper when the server
copy is newer than the saved file. Download all listed saves the whole
filtered list with progress, and Stop cancels it. Every downloaded
paper is validated before it is saved. The Mock Test setup lists every
paper with its origin and deletes downloaded ones.

Run the reference server (stdlib Python only, see
`tools/content-server/README.md`):

```powershell
cd tools/content-server
python server.py --papers ../../Content/Assets/Exams --port 8765
python server.py --papers ./papers --audio ./audio --code SECRET
python server.py --papers ./papers --user teacher --password SECRET
```

Offline models go in `Content/Assets/Models`. The list of models the app
uses, with source and license, is in `Content/Assets/Models/README.md`.
Missing files are fine. Objective parts still score offline, speaking
answers fall back to a typed transcript, and Writing falls back to AI
marking alone.

## Language model (optional)

Open Settings and enter:

- Base URL, for example `http://localhost:11434/v1` for Ollama
- Model name, for example `llama3.1`
- API key, only if the server needs one

Press Save, then Test connection to confirm the server answers. The key
is encrypted on this computer. Without a model, AI bands, AI topic
ideas, and Reading explanations stay disabled. Listening, Reading
scoring, and the full test flow always work offline. If a model is set
but the server is down, the app falls back to the offline path instead
of failing.

Turn on the vision option only for a model that reads images, such as
gpt-4o or llava. It is currently unused by the test flow.

## Building the offline models

Four slots, each fetched once on a dev machine with its script:

- Pronunciation: `tools/speaking-mdd` (phoneme scoring).
- Transcription: `tools/speech-stt` (whisper-tiny.en, speaking to text).
- Grammar: `tools/writing-gec` (gec-t5_small, strict Writing marking).
- Examiner voice: `tools/speaking-tts` (Piper voice plus espeak-ng).

Example for the grammar model:

```powershell
cd tools/writing-gec
conda env create -p .venv -f environment.yml
conda activate .\.venv
optimum-cli export onnx --model Unbabel/gec-t5_small --task text2text-generation --output ./models/fp32
python export_onnx.py
```

Copy the `*-int8.onnx` files plus their companions
(`mdd-labels.json`, `stt-whisper-tiny-en-vocab.json`,
`gec-t5-spiece.model`, `tts-piper-lessac-medium.onnx.json` and the
`espeak-ng/` folder) into `Content/Assets/Models/`. The full steps are in
each tool folder README and in `Content/Assets/Models/README.md`.

## Project layout

```
IELTop.Core/       shared library: models, data, services, exam engine
IELTop.Desktop/    the app: Photino host, JSON bridge, React web UI
  Bridge/          method router between the web UI and C#
  UserInterface/   React + Vite source
  wwwroot/         built UI, written by npm build, not committed
Content/           shared read only content: exams, audio, images, models
IELTop_Content_Server/  optional ASP.NET content server with an admin portal
tools/             Python helpers, each with its own conda environment
```

## License

See the repository license. Model files keep their own licenses, listed in
`Content/Assets/Models/README.md`.
