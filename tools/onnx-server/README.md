# ONNX model server

Serves the offline ONNX models over loopback HTTP, so another program can use
them without linking IELTop or the native runtime.

## Run it

```powershell
IELTop.Desktop\bin\Debug\net10.0-windows10.0.19041.0\win-x64\IELTop.Desktop.exe --model-server
```

No window opens. The server binds `http://127.0.0.1:8770` and writes
`%LocalAppData%\IELTop\model-server.json` with the url and a bearer token (the
windowed app has no visible console). The file is removed when the server stops.
Set `IELTOP_LOG` to see request lines in `%LocalAppData%\IELTop\logs\latest.log`.

## Auth

Every request needs `Authorization: Bearer <token>` from the info file. The
server refuses any non loopback client. The token is a local convenience gate,
not protection against a process running as the same user.

## Routes

| Method | Path | Body | Reply |
|--------|------|------|-------|
| GET | `/health` | - | which slots are installed/loaded, and which features are ready |
| POST | `/v1/stt` | `{ "audioBase64": "<16k mono wav>" }` | `{ "text": "..." }` |
| POST | `/v1/tts` | `{ "text": "..." }` | `{ "voice": "...", "wavBase64": "..." }` |
| POST | `/v1/pronunciation/check` | `{ "audioBase64": "...", "targetText": "..." }` | pronunciation result with edits, accuracy, and `meanGop` |
| POST | `/v1/grammar/check` | `{ "text": "..." }` | corrected text, edits, error count |

Audio is uploaded as base64, not a path, so the server never reads a file the
client names.

## Client example

```powershell
$cfg = Get-Content "$env:LocalAppData\IELTop\model-server.json" | ConvertFrom-Json
$h = @{ Authorization = "Bearer $($cfg.token)" }

Invoke-RestMethod "$($cfg.url)/health" -Headers $h

$body = @{ text = "he go to school yesterday" } | ConvertTo-Json
Invoke-RestMethod "$($cfg.url)/v1/grammar/check" -Method Post -Headers $h `
    -ContentType 'application/json' -Body $body
```

```csharp
using var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new("Bearer", token);
var reply = await http.PostAsJsonAsync($"{url}/v1/grammar/check", new { text = "he go to school yesterday" });
```

## Notes

- Models load lazily on first use and stay in memory. STT and pronunciation are
  the slow ones to load (5 to 15 s on CPU); later calls are fast.
- The model files live in `Content/Assets/Models` and are copied to the output by
  `IELTop.Desktop.csproj`. `.onnx` files are not in git. The registry in
  `IELTop.Core/Services/Ai/OnnxModelRegistry.cs` is the single list of slots, and
  a slot is usable only when every file it declares exists.
