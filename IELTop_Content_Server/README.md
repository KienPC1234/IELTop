# IELTop content server

The IELTop content server hands mock test papers and audio clips to the
app over the `ieltop/1` protocol. It also runs a portal where people
submit new practice content and where the team reviews and publishes it.

It is an ASP.NET Core app on .NET 10. The same build runs on a laptop
with SQLite and on a Linux server with Postgres and Redis.

## What is in it

- **Protocol API** the app reads: `/api/info`, `/api/papers`,
  `/api/papers/{id}`, `/api/audio/{file}`, `/api/login`.
- **Admin portal** for the team: dashboard, review queue, catalog,
  papers, audio, contributors, editor applications, client access,
  settings, outbox, activity, health.
- **Contributor portal** for the public: register, sign in, submit
  content, track submissions, apply to become an editor.
- **Public pages**: about the server and a content and licensing page.

## Run it locally

```powershell
cd IELTop_Content_Server
dotnet run
```

Open `http://localhost:5074`.

- Admin sign in: `/Account/Login`, `admin` / `admin123` in Development.
- Contributor sign in: `/Contrib/SignIn`, register at `/Contrib/Register`.

Change the admin password before you put this anywhere public. The seed
admin is created once, the first time the user table is empty.

On first start the server creates `App_Data/`, the database, an admin
user, and imports the sample papers from `../IELTop/Assets/Exams`.

## The protocol

| Method | Path | Auth | Notes |
| --- | --- | --- | --- |
| GET | `/api/info` | never | greeting: name, protocol, auth modes, skills |
| GET | `/api/papers` | guarded | list, filters `?skill=&category=&q=` |
| GET | `/api/papers/{id}` | guarded | one full paper as stored |
| GET | `/api/audio/{file}` | guarded | clip, range requests and ETag |
| POST | `/api/login` | never | `{username,password}` returns `{token}` |

A guarded call is allowed when anonymous mode is on, or the caller sends
`X-Access-Code: <code>`, or sends `Authorization: Bearer <token>` from a
successful login.

The list fields match what the app reads: id, title, category, level,
skills, parts, questions, updated, size.

## Contribute flow

This is how new content enters the library without an admin touching a
database.

1. **Register** at `/Contrib/Register`. A Cloudflare Turnstile check runs
   when it is configured, and a welcome email is queued.
2. **Submit** at `/Contrib/Submit`. A submission can hold several files:
   the paper as JSON, or as txt, md, csv, docx, or pdf text, plus
   listening clips as m4a, opus, ogg, wav, mp3, aac, or flac. The author
   must confirm they have the right to share the content.
3. **Automated review**. When a model is configured, it reads the paper,
   gives a score, suggests tags, and lists problems. It can only reject
   outright when the operator sets an auto reject floor. A broken model
   never blocks a submission: it waits for a human with no score.
4. **Human review**. The queue is at `/Submissions`. An admin or editor
   accepts, and the paper is published to the protocol store and the
   catalog, or rejects it with a reason.
5. **Email**. The author gets an email either way, plus one when their
   editor application is decided.

Paper ids are derived from the title and made unique, so a submission
never overwrites an existing paper unless it is meant to.

## Becoming an editor

Anyone can apply at `/Contrib/ApplyEditor`. An admin decides in
`/Editors`. On approval the applicant gets a contributor account with a
temporary password by email, and their role is recorded. Editors help
sign in to review the queue.

## Admin portal

- **Dashboard** connection address, auth modes, runtime info, counts,
  today's traffic, recent activity.
- **Review queue** submissions by status, with the model review beside
  the files and one click to accept or reject.
- **Catalog** published papers with author, tags, license, and counts.
- **Papers** and **Audio** the protocol store, for direct edits.
- **Contributors** accounts, activate, deactivate, block.
- **Editor applications** approve with a role, or decline.
- **Client access** access codes and login accounts for the app.
- **Settings** server name, skills, auth modes, admin accounts, and a
  separate **Email** page for SMTP with a test send.
- **Outbox** every queued notification and its delivery state.
- **Activity** the audit trail of admin and contributor actions.
- **Health** a public self check with no secrets.

## Configuration

Settings come from `appsettings.json`, environment variables, or the
portal. Environment variables use the section name with double
underscores, for example `Server__Name` or `Database__ConnectionString`.

```jsonc
{
  "Server": {
    "Name": "My IELTS server",
    "AllowAnonymous": false,
    "AllowAccessCode": true,
    "AllowLogin": true,
    "MasterCode": "",
    "TokenHours": 12,
    "AdminUsername": "admin",
    "AdminPassword": "",
    "Skills": ["listening", "reading", "writing", "speaking"]
  },
  "Database": { "Provider": "Sqlite", "ConnectionString": "" },
  "Cache": { "Provider": "Memory", "RedisConnection": "localhost:6379" },
  "Storage": { "Root": "App_Data", "SeedFolder": "", "ImportFolder": "", "MaxUploadMb": 300 },
  "RateLimit": { "Enabled": true, "AnonymousPerMinute": 600, "CredentialPerMinute": 3000 },
  "Captcha": { "SiteKey": "", "SecretKey": "", "AllowTestKeys": true },
  "Smtp": {
    "Enabled": false, "Host": "", "Port": 587, "UseStartTls": true,
    "Username": "", "Password": "",
    "FromAddress": "no-reply@example.com", "FromName": "IELTop Content Server", "BaseUrl": ""
  },
  "Llm": { "BaseUrl": "", "ApiKey": "", "Model": "", "Temperature": 0.2, "MaxTokens": 2000, "TimeoutSeconds": 120 },
  "Contribute": {
    "AutoReview": true, "AutoRejectBelowScore": 0,
    "MaxFilesPerSubmission": 10, "MaxSubmissionMb": 60, "SubmissionsPerHour": 10
  }
}
```

### Cloudflare Turnstile

Set `Captcha__SiteKey` and `Captcha__SecretKey` to turn the bot check on
for register, sign in, submit, and the editor application. Leave both
empty and the forms work without it, so the server runs offline.

Cloudflare publishes always-pass keys for development. The server only
honors them in Development:

- Site key: `1x00000000000000000000AA`
- Secret key: `1x0000000000000000000000000000000AA`

When Turnstile is on, the portal sends a Content Security Policy that
allows `challenges.cloudflare.com` for the script, frame, and connect
sources, and nothing else.

### SMTP

Set `Smtp__Enabled` true and fill in the host. The password can also be
set from the Email settings page; leave the field blank there to keep
the stored one. If SMTP is off, notifications queue up and go out the
moment it is turned on. A down mail host only delays mail, it never
blocks a request: a background worker retries a few times, then marks
the row failed with the reason, all visible in the Outbox.

### Language model review

Any OpenAI compatible chat endpoint works: OpenAI, Azure compatible
gateways, Ollama, LM Studio, llama.cpp server, vLLM. Point
`Llm__BaseUrl` at the `/v1` root, set `Llm__Model` and, if needed,
`Llm__ApiKey`. With those empty the review is skipped and submissions
wait for a human. The model never sees anything except the paper text.

`Contribute__AutoRejectBelowScore` is the only way the model can reject
on its own. Leave it at 0 so every submission gets a human look, or set
it to say 40 to drop clearly bad ones automatically.

## Tuning for many clients

- Paper lists and bodies are cached in memory, then in the shared cache.
  Any content change bumps a version number, so stale entries are never
  served.
- `ContentCache` collapses a stampede: a hundred clients asking for a
  cold list hit the database once.
- Audio streams from disk with range support, so the kernel sends the
  bytes and a player can seek.
- Responses are Brotli or gzip compressed.
- Rate limiting is per caller. Anonymous callers share an address bucket
  at `AnonymousPerMinute`. A code or token gets its own bucket at
  `CredentialPerMinute`, so one busy class cannot starve another.
- Stats stay in memory and flush to the store on a timer, so a download
  never waits on a database write.
- Kestrel has no connection cap, a two minute keep alive, and no
  response size limit. Request bodies are capped by the storage and
  submission limits.

### Scale out

Run several instances behind a load balancer, point them at one Postgres
and one Redis. Redis carries the cache, cache version, and login tokens,
and the data protection key ring lives under `Storage__Root`, so a
client that signs in on one instance works on the next.

```jsonc
{
  "Database": { "Provider": "Postgres", "ConnectionString": "Host=db;Database=ieltop;Username=ieltop;Password=..." },
  "Cache": { "Provider": "Redis", "RedisConnection": "redis:6379" },
  "Storage": { "Root": "/var/ieltop" }
}
```

Uploaded audio and the data protection keys must live on a shared volume
across instances, or be served by one host. The database stores metadata.

## Security

- Admin and login passwords are PBKDF2 hashed. Contributor passwords too.
- Access codes are stored as typed so you can hand them out again. They
  are a classroom key, not a secret vault.
- Two separate cookie schemes: `ieltop.admin` for the portal and
  `ieltop.contrib` for the public side. A contributor can never open an
  admin page, and the admin cookie cannot be used on the public side.
- Every admin and contributor page resolves the signed in user from the
  cookie, never from an id in the query string.
- Uploads are written to a temporary file first, then moved, so a broken
  upload never replaces a working clip.
- A paper or a clip is validated before it is stored. Bad files are
  skipped with a reason.
- File paths are rebuilt from stored values and checked to stay under
  the storage root, so no request can reach another file.
- Anti forgery tokens are on every form; antiforgery, auth, and data
  protection cookies are HttpOnly and SameSite Lax.
- Every response carries `X-Content-Type-Options`, `X-Frame-Options`,
  `Referrer-Policy`, `Permissions-Policy`, and on form pages a tight CSP.
- The admin portal never displays a secret. SMTP passwords and model keys
  are edited on the host, or written without ever being read back.

## Files that matter

```
IELTop_Content_Server/
  Program.cs              host, options, middleware, rate limit, schemes
  ProtocolApi.cs          the ieltop/1 endpoints
  DbInitializer.cs        first run seed
  Data/                   EF Core context and value converters
  Models/                 entities
  Options/                typed configuration
  Services/               papers, audio, cache, auth, settings, audit,
                          contributors, submissions, review, notifications,
                          editors, stats, captcha, text extractor
  Pages/                  the admin and contributor portals
```

## Docker

The included `Dockerfile` builds a Linux image. The default in
`appsettings.json` uses SQLite, so mount the store for that, or override
the environment for Postgres and Redis:

```bash
docker build -t ieltop-content-server .
docker run -p 8080:8080 \
  -e Server__AdminPassword=change-me \
  -e Database__Provider=Postgres \
  -e Database__ConnectionString='Host=db;Database=ieltop;Username=ieltop;Password=...' \
  -e Cache__Provider=Redis -e Cache__RedisConnection=redis:6379 \
  -v ieltop-data:/app/App_Data \
  ieltop-content-server
```

Put this behind a reverse proxy that terminates TLS. The app can use
plain `http://` on a trusted network, or `https://` with a certificate
the machine trusts. For a self signed certificate the app has an Allow
self signed certificate option per server.

## Data and safety

- `App_Data/` is yours: the database, uploaded audio, submissions, and
  the data protection key ring. Back it up. It is ignored by git.
- Submitted files stay with the submission so a review is reproducible.
- Every accepted paper keeps its author and license in the catalog.
- Any band or score is a practice estimate, not an official IELTS result.
