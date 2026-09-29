# IELTop content server

Reference server for protocol `ieltop/1`. Stdlib Python only, no
install needed. It serves mock test papers and audio clips to the
Servers page in the app.

## Endpoints

- `GET /api/info` paper list greeting, always open
- `GET /api/papers` paper summaries, with `?skill=`, `?category=`,
  `?q=` filters. Each summary carries id, title, category, level,
  skills, parts, questions, updated date, and size in bytes.
- `GET /api/papers/{id}` one full paper (`{id}.json`)
- `GET /api/audio/{file}` one audio clip
- `POST /api/login` `{username, password}` returns `{token}`

Guarded endpoints need `X-Access-Code` or `Authorization: Bearer`.

## Run

```powershell
cd tools/content-server
python server.py --papers ../../IELTop/Assets/Exams --port 8765
python server.py --papers ./papers --audio ./audio --code SECRET
python server.py --papers ./papers --user teacher --password SECRET
```

HTTPS with your own certificate:

```powershell
python server.py --papers ./papers --port 8766 --cert cert.pem --key key.pem
```

In the app, https addresses work as-is. For a self signed
certificate, tick Allow self signed certificate on that server.

Then open the Servers page in the app and press Connect on
`IELTop Sample Server`, or add `http://localhost:8765` yourself.

Downloaded papers land in `%LocalAppData%/IELTop/content/Exams` with
audio in `%LocalAppData%/IELTop/content/Audio`, so they show up in Mock
Test right away and survive app updates.
