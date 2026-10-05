# Debugging the desktop app

Windows only. The app is a MAUI WinUI host with a React UI inside a WebView2.
Most faults are found by reading one log file, not by opening a window again.

## 1. One command that checks everything, no window

```powershell
dotnet build IELTop.slnx --nologo -v minimal
IELTop.Desktop\bin\Debug\net10.0-windows10.0.19041.0\win-x64\IELTop.Desktop.exe --selftest
```

Runs database, settings, lessons, lesson search, papers, exam engine, models,
several bridge routes, the log tail, and the log file. Prints one line per check
and exits 0 when all pass, 2 when any fail. No window opens.

## 2. The log

- `%LocalAppData%\IELTop\logs\latest.log` is the newest run.
- `%LocalAppData%\IELTop\logs\ieltop-YYYYMMDD-HHMMSS.log` is one file per run
  (the last 12 are kept).
- Every line has time to the millisecond, level, thread, category, message.
  Errors carry the full stack and the inner exception.
- Every bridge call is logged: a start line (Trace), a finish line with the
  milliseconds (Debug), and a failure (Error). Arguments are summarized as
  sizes, never as full text, so an API key or an essay never lands in the file.
- Uncaught page errors are sent to the host and written as `web: Page error`.

The level comes from `IELTOP_LOG` (`trace`, `debug`, `info`, `warn`, `error`) and
can be changed live from the Diagnostics tab. At `Info` the per-call lines are
not written; use `Debug` or `Trace` to see them.

Read the log without leaving the shell:

```powershell
Get-Content "$env:LocalAppData\IELTop\logs\latest.log" -Tail 60
Select-String -Path "$env:LocalAppData\IELTop\logs\latest.log" -Pattern "ERROR"
```

## 3. The Diagnostics tab (Settings)

Session health badge, bridge call counters, slowest calls, database check, log
level, a marker button, Export log, Open log folder, Copy log, and a live log
tail. One look says whether a session is healthy.

## 4. Driving the real UI without showing a window

WebView2 opens a debug port. The shell here is often elevated, and an elevated
process ignores the port environment variable, so launch through `explorer.exe`.

```powershell
# Start with the debug port open (de-elevated, from an elevated shell).
Start-Process explorer.exe "C:\Users\kienp\source\repos\IELTop\tools\debug-desktop\launch.cmd"

# Run JavaScript in the page and print the value.
pwsh -File tools\debug-desktop\cdp.ps1 -Js "document.title"
pwsh -File tools\debug-desktop\cdp.ps1 -Click "Diagnostics" -Js "[...document.querySelectorAll('button')].map(b=>b.textContent.trim()).slice(0,6)"
pwsh -File tools\debug-desktop\cdp.ps1 -Shot "$env:Temp\shot.png"
```

`cdp.ps1` waits for the page and the `window.__ieltopsBridge` object, dispatches
the same pointer events Radix expects, and never prints the page access key.

Useful page-side probes:

- `JSON.stringify(await window.__ieltopsBridge.invokeMethodAsync('CallAsync','x','diagnostics.snapshot','{\"tailLines\":200}'))`
- `!!window.__ieltopsBridge` to confirm the Blazor channel is up.
- `PerformanceObserver` / `performance.getEntriesByType('resource')` for load time.

## 5. Tests

```powershell
dotnet test IELTop.Tests\IELTop.Tests.csproj --nologo
```

Pure logic only, no window, no model. When `llm.txt` exists at the repo root,
two extra tests call a real OpenAI compatible model (from `llm.txt`) to prove the
tutor is grounded in the lesson and the practice builder returns usable JSON:

```powershell
dotnet test IELTop.Tests\IELTop.Tests.csproj --nologo --filter "FullyQualifiedName~StudyLiveLlmTests" -v normal
```

`llm.txt` is gitignored and holds the base URL, the key, and the model names.
Never print the key and never commit the file.

## 6. When something is wrong

1. Run `--selftest`; read the failing line.
2. Open `latest.log`; find the `ERROR` line and the stack under it.
3. If it is a page fault, the log has `web: Page error: ...` with the stack.
4. If it is a flow, drive it with `cdp.ps1` and watch the bridge lines in the log
   for the exact call, its arguments summary, and its timing.
5. Settings, Diagnostics tab, Mark button: drop a marker, reproduce, read the
   tail around the marker.
