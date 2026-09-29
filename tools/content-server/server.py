"""IELTop reference content server (protocol ieltop/1). Stdlib only.

Serves mock test papers and audio over plain HTTP:

    GET  /api/info            server greeting, never needs auth
    GET  /api/papers          paper list, needs auth when guarded.
                              Filters: ?skill=Reading&category=Academic&q=city
    GET  /api/papers/{id}     one full paper, needs auth when guarded
    GET  /api/audio/{file}    one audio clip, needs auth when guarded
    POST /api/login           {username, password} -> {token}

Examples:

    python server.py --papers ../../IELTop/Assets/Exams --port 8765
    python server.py --papers ./papers --audio ./audio --code SECRET
    python server.py --papers ./papers --user teacher --pass SECRET

Auth modes: anonymous when no flags are given, access code with
--code, login with --user/--pass. Both flags can be combined.
"""

import argparse
import hashlib
import json
import secrets
import ssl
import urllib.parse
from datetime import datetime
from functools import partial
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

PROTOCOL = "ieltop/1"
AUDIO_TYPES = {".wav": "audio/wav", ".mp3": "audio/mpeg", ".m4a": "audio/mp4"}


class Options:
    def __init__(self, args):
        self.name = args.name
        self.papers = Path(args.papers)
        self.audio = Path(args.audio) if args.audio else None
        self.code = args.code or ""
        self.user = args.user or ""
        self.password = args.password or ""
        self.tokens = set()


def paper_summary(path: Path):
    try:
        paper = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    parts = paper.get("parts", [])
    if not paper.get("title") or not parts:
        return None
    skills = sorted({p.get("skill", "") for p in parts if p.get("skill")})
    questions = sum(len(p.get("questions", [])) for p in parts)
    try:
        updated = datetime.fromtimestamp(path.stat().st_mtime).strftime("%Y-%m-%d")
    except OSError:
        updated = ""
    try:
        size = path.stat().st_size
    except OSError:
        size = 0
    return {
        "id": path.stem,
        "title": paper["title"],
        "category": paper.get("category", ""),
        "level": paper.get("level", ""),
        "skills": skills,
        "parts": len(parts),
        "questions": questions,
        "updated": updated,
        "size": size,
    }


def matches_query(summary, query):
    """skill, category, and q filters from ?skill=&category=&q= ."""
    skill = query.get("skill", [""])[0].strip().lower()
    if skill and skill not in [s.lower() for s in summary["skills"]]:
        return False
    category = query.get("category", [""])[0].strip().lower()
    if category and category != summary.get("category", "").lower():
        return False
    text = query.get("q", [""])[0].strip().lower()
    if text and text not in summary["title"].lower():
        return False
    return True


def auth_modes(options):
    modes = []
    if options.code:
        modes.append("code")
    if options.user:
        modes.append("login")
    if not modes:
        modes.append("anonymous")
    return modes


class Handler(BaseHTTPRequestHandler):
    options = None
    server_version = "IELTopContentServer/1"

    def log_message(self, *args):
        pass

    def _send(self, status, payload=None, content_type="application/json", raw=None):
        body = raw if raw is not None else json.dumps(payload if payload is not None else []).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _auth_modes(self):
        return auth_modes(self.options)

    def _authorized(self):
        if "anonymous" in self._auth_modes() and not self.options.code and not self.options.user:
            return True
        if self.options.code and self.headers.get("X-Access-Code") == self.options.code:
            return True
        auth = self.headers.get("Authorization", "")
        if auth.startswith("Bearer ") and auth[7:].strip() in self.options.tokens:
            return True
        return False

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        parts = [p for p in parsed.path.split("/") if p]

        if parts == ["api", "info"]:
            return self._send(200, {
                "name": self.options.name,
                "protocol": PROTOCOL,
                "auth": self._auth_modes(),
                "skills": ["listening", "reading", "writing", "speaking"],
            })

        if not self._authorized():
            return self._send(401, {"error": "access denied"})

        if parts == ["api", "papers"]:
            query = urllib.parse.parse_qs(parsed.query)
            papers = []
            if self.options.papers.is_dir():
                for path in sorted(self.options.papers.glob("*.json")):
                    summary = paper_summary(path)
                    if summary and matches_query(summary, query):
                        papers.append(summary)
            return self._send(200, papers)

        if len(parts) == 3 and parts[:2] == ["api", "papers"]:
            path = (self.options.papers / (parts[2] + ".json")).resolve()
            if path.suffix != ".json" or self.options.papers.resolve() not in path.parents:
                return self._send(404, {"error": "not found"})
            if not path.is_file():
                return self._send(404, {"error": "not found"})
            return self._send(200, raw=path.read_bytes())

        if len(parts) == 3 and parts[:2] == ["api", "audio"]:
            if self.options.audio is None:
                return self._send(404, {"error": "not found"})
            path = (self.options.audio / parts[2]).resolve()
            if self.options.audio.resolve() not in path.parents or not path.is_file():
                return self._send(404, {"error": "not found"})
            ctype = AUDIO_TYPES.get(path.suffix.lower(), "application/octet-stream")
            return self._send(200, content_type=ctype, raw=path.read_bytes())

        return self._send(404, {"error": "not found"})

    def do_POST(self):
        parsed = urllib.parse.urlparse(self.path)
        if [p for p in parsed.path.split("/") if p] != ["api", "login"]:
            return self._send(404, {"error": "not found"})
        if not self.options.user:
            return self._send(404, {"error": "not found"})
        try:
            length = int(self.headers.get("Content-Length", 0))
            body = json.loads(self.rfile.read(length or 0) or b"{}")
        except ValueError:
            return self._send(400, {"error": "bad request"})
        ok = (
            body.get("username") == self.options.user
            and hashlib.sha256(str(body.get("password", "")).encode()).hexdigest()
            == hashlib.sha256(self.options.password.encode()).hexdigest()
            and self.options.password
        )
        if not ok:
            return self._send(401, {"error": "access denied"})
        token = secrets.token_hex(16)
        self.options.tokens.add(token)
        return self._send(200, {"token": token})


def main():
    args_parser = argparse.ArgumentParser(description="IELTop content server")
    args_parser.add_argument("--papers", required=True, help="folder with paper *.json files")
    args_parser.add_argument("--audio", default="", help="folder with audio clips")
    args_parser.add_argument("--port", type=int, default=8765)
    args_parser.add_argument("--name", default="IELTop Server")
    args_parser.add_argument("--code", default="", help="require this access code")
    args_parser.add_argument("--user", default="", help="require login with this username")
    args_parser.add_argument("--password", default="", help="password for --user")
    args_parser.add_argument("--cert", default="", help="PEM certificate file to serve https")
    args_parser.add_argument("--key", default="", help="PEM private key file for --cert")
    args = args_parser.parse_args()

    Handler.options = Options(args)
    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    if args.cert:
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.load_cert_chain(args.cert, args.key or None)
        server.socket = context.wrap_socket(server.socket, server_side=True)
        scheme = "https"
    else:
        scheme = "http"
    print(f"serving {args.papers} at {scheme}://localhost:{args.port} auth={auth_modes(Handler.options)}")
    server.serve_forever()


if __name__ == "__main__":
    main()
