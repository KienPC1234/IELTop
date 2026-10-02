# IELTop

Offline-first IELTS mock test app on .NET 10. A Photino native window hosts a React web UI, and shared logic lives in `IELTop.Core`. It runs on Windows, macOS, and Linux.

Band scores are practice estimates only. They are not official IELTS scores.

## Screens

- Overview: streak, recent bands, and study activity.
- Mock Test: pick a paper, pick skills, run a timed test, review answers.
- Library: browse papers, import files, draft a custom test for the Editor.
- Editor: edit a paper, preview AI drafted questions, save or export.
- Results: past attempts with band ranges and Writing/Speaking criteria.
- Servers: download papers from community or private content servers.
- Settings: language model, offline models, audio devices, theme, updates.

## Quick start

Requirements:

- .NET 10 runtime.
- WebView2 on Windows, WebKitGTK on Linux, WKWebView on macOS.
- Node.js 20 or newer, only to build the web UI.
- Microphone for Speaking, speakers or headphones for Listening.

Build and run from the repo root:

```powershell
dotnet build IELTop.slnx
dotnet run --project IELTop.Desktop
```

`dotnet build` also builds the web UI with npm, so one command is enough. The UI is served from a loopback address inside the app process. Online features (content servers, language model, update check) are optional; the test flow works offline.

## Test papers

Papers are plain JSON in `Content/Assets/Exams/`. Each paper has a title, a `source`, and parts with `skill`, `minutes`, `material`, and `questions`. See `Content/Assets/Exams/README.md` for the full format.

Listening plays its audio clip once. When a paper ships no audio file, the app shows the transcript instead. Papers you download land under `%LocalAppData%/IELTop/content/` and appear in Mock Test right away.

## Scoring and review

- Every band shows as a range, for example 6.0 to 7.0.
- Marking strictness (Lenient, Standard, Strict) only changes scoring.
- Optional Strict mode locks the exam window to fullscreen and counts focus loss as a violation.
- Listening and Reading score locally with no model. Writing collects essays for teacher or AI marking. Speaking records audio and also accepts a typed transcript.
- After submit, open the review to see answers and explanations. With a language model set, the app adds AI marking and Reading explanations.

## Language model (optional)

Settings accepts any OpenAI compatible chat server (OpenAI, Ollama, LM Studio, llama.cpp server, vLLM). Fill in Base URL, Model, API key if needed, Temperature, Max tokens, and the rest, then press Test connection. The key is stored encrypted on the machine.

Without a model, AI marking and AI drafted questions stay disabled and the app falls back to the offline path. The vision switch is off by default and only matters for models that read images.

## Offline models (optional)

Model files go in `Content/Assets/Models/`. The slot list with source and license is in `Content/Assets/Models/README.md` and is also shown in Settings. Missing files are a normal state: the app keeps working and falls back (typed transcript for Speaking, AI marking alone for Writing).

## Content servers (optional)

The Servers page speaks protocol `ieltop/1` over HTTP and downloads papers plus audio. The shipped list is in `Content/servers.txt`. Servers can be open, need an access code, or need a username and password. Secrets stay encrypted on the machine.

- Minimal reference server: `tools/content-server/README.md`.
- Full server with admin and contributor portals: `IELTop_Content_Server/README.md`.

## Release

```powershell
pwsh tools/pack-release.ps1 -Version 1.0.1
pwsh tools/pack-release.ps1 -Version 1.0.1 -Runtime linux-x64
```

Output is `releases/IELTop-<version>-<runtime>.zip`. Attach it to a GitHub Release tagged `v<version>`; the app checks GitHub Releases and opens the release page on update.

## Project layout

```
IELTop.Core/            shared logic: models, data, exam engine, AI, app services
IELTop.Desktop/         client: Photino host, JSON bridge, React web UI
  Bridge/               method router between web UI and C#
  UserInterface/        React + Vite source
  wwwroot/              built UI, written by npm build, not committed
IELTop.Tests/           xUnit tests for Core logic
Content/                read only content: exams, audio, images, models, servers.txt
IELTop_Content_Server/  optional ASP.NET content server with admin portal
tools/                  Python helpers, each with its own conda environment
```

## License

AGPL, see `LICENSE.txt`. Model files keep their own licenses, listed in `Content/Assets/Models/README.md`.
