"""Build a pre-populated papers template DB from the repo JSON seeds.

Reads Content/Assets/Exams/*.json and writes a SQLite file with the same
ExamPapers/ExamParts/ExamQuestions tables the app uses at runtime, so every
build ships library data sourced from the repo. The app copies this template
on first start when no database exists yet; later starts reseed from JSON.

Table and column layout mirrors IELTop.Core Models/ExamStoreModels.cs and the
sqlite-net defaults (TEXT columns, INTEGER value columns, INTEGER PK).
Only the standard library is used.

Usage:
    python tools/seed-db/seed.py <exams_dir> <out_db>
"""

import hashlib
import json
import os
import sqlite3
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path

DOTNET_TICKS_AT_UNIX_EPOCH = 621355968000000000


def dotnet_ticks_now() -> int:
    dt = datetime.now(timezone.utc)
    return DOTNET_TICKS_AT_UNIX_EPOCH + int(dt.timestamp() * 10_000_000)


def file_hash(raw: bytes) -> str:
    # Mirrors C# ExamRepository.HashOf(File.ReadAllText): the reader strips a
    # BOM, so decode losslessly first and hash the plain UTF-8 bytes.
    text = raw.decode("utf-8-sig")
    return hashlib.sha256(text.encode("utf-8")).hexdigest().upper()


SCHEMA = """
CREATE TABLE IF NOT EXISTS "ExamPapers"(
  "Id" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
  "Title" TEXT, "Source" TEXT, "Category" TEXT, "Level" TEXT,
  "TagsJson" TEXT, "Origin" TEXT, "ContentHash" TEXT,
  "UpdatedAt" INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS "ExamParts"(
  "Id" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
  "PaperId" INTEGER NOT NULL, "Order" INTEGER NOT NULL,
  "PartCode" TEXT, "Skill" TEXT, "Title" TEXT, "Topic" TEXT,
  "TaskType" TEXT, "Material" TEXT, "Instructions" TEXT,
  "AudioFile" TEXT, "ImageFile" TEXT,
  "Minutes" INTEGER NOT NULL, "PrepSeconds" INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS "ExamQuestions"(
  "Id" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
  "PartId" INTEGER NOT NULL, "Number" INTEGER NOT NULL,
  "Kind" TEXT, "Prompt" TEXT, "CorrectKey" TEXT, "GapAnswer" TEXT,
  "Explanation" TEXT, "OptionsJson" TEXT, "MatchRowsJson" TEXT, "BankJson" TEXT);
"""


def get(obj: dict, *names: str, default=""):
    """Case-insensitive field read. Repo seeds use lowercase keys
    (title, parts, skill) while C# models read case-insensitively."""
    lowered = {str(k).lower(): v for k, v in obj.items()}
    for name in names:
        key = str(name).lower()
        if key in lowered:
            value = lowered[key]
            return value if value is not None else default
    return default


def load_papers(exams_dir: Path):
    papers = []
    for path in sorted(exams_dir.glob("*.json")):
        try:
            raw = path.read_bytes()
            paper = json.loads(raw.decode("utf-8-sig"))
        except (OSError, ValueError):
            continue
        if not isinstance(paper, dict) or not get(paper, "Parts"):
            continue
        papers.append((paper, file_hash(raw)))
    return papers


def insert_paper(conn: sqlite3.Connection, paper: dict, content_hash: str, now_ticks: int):
    cur = conn.execute(
        "INSERT INTO ExamPapers "
        "(Title, Source, Category, Level, TagsJson, Origin, ContentHash, UpdatedAt)"
        " VALUES (?, ?, ?, ?, ?, 'shipped', ?, ?)",
        (
            get(paper, "Title"),
            get(paper, "Source"),
            get(paper, "Category"),
            get(paper, "Level"),
            json.dumps(get(paper, "Tags") or [], ensure_ascii=False),
            content_hash,
            now_ticks,
        ),
    )
    paper_id = cur.lastrowid
    for order, part in enumerate(get(paper, "Parts") or []):
        cur = conn.execute(
            "INSERT INTO ExamParts "
            "(PaperId, [Order], PartCode, Skill, Title, Topic, TaskType,"
            " Material, Instructions, AudioFile, ImageFile, Minutes, PrepSeconds)"
            " VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            (
                paper_id,
                order,
                get(part, "Id"),
                get(part, "Skill"),
                get(part, "Title"),
                get(part, "Topic"),
                get(part, "TaskType"),
                get(part, "Material"),
                get(part, "Instructions"),
                get(part, "AudioFile"),
                get(part, "ImageFile"),
                int(get(part, "Minutes") or 10),
                int(get(part, "PrepSeconds") or 0),
            ),
        )
        part_id = cur.lastrowid
        for question in get(part, "Questions") or []:
            kind = get(question, "Kind") or "choice"
            options = [
                {"Key": get(o, "Key"), "Text": get(o, "Text")}
                for o in (get(question, "Options") or [])
                if isinstance(o, dict)
            ]
            match_rows = [
                {"Label": get(r, "Label"), "Answer": get(r, "Answer")}
                for r in (get(question, "MatchRows", get(question, "Rows")) or [])
                if isinstance(r, dict)
            ]
            conn.execute(
                "INSERT INTO ExamQuestions "
                "(PartId, Number, Kind, Prompt, CorrectKey, GapAnswer,"
                " Explanation, OptionsJson, MatchRowsJson, BankJson)"
                " VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                (
                    part_id,
                    int(get(question, "Number") or 0),
                    kind,
                    get(question, "Prompt"),
                    get(question, "CorrectKey"),
                    get(question, "GapAnswer"),
                    get(question, "Explanation"),
                    json.dumps(options, ensure_ascii=False),
                    json.dumps(match_rows, ensure_ascii=False),
                    json.dumps(get(question, "Bank") or [], ensure_ascii=False),
                ),
            )


def main(argv: list) -> int:
    if len(argv) != 3:
        print("usage: seed.py <exams_dir> <out_db>", file=sys.stderr)
        return 2
    exams_dir = Path(argv[1])
    out_db = Path(argv[2])
    papers = load_papers(exams_dir)
    if not papers:
        # Mirrors ReadFolder: a missing or empty seed folder is not an error.
        # The app still seeds from JSON at first run (usually just empty too).
        print(f"seed: no papers under {exams_dir}, template not written", file=sys.stderr)
        return 0

    out_db.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp_name = tempfile.mkstemp(prefix=out_db.name, dir=str(out_db.parent))
    os.close(fd)
    try:
        conn = sqlite3.connect(tmp_name)
        try:
            conn.executescript(SCHEMA)
            now_ticks = dotnet_ticks_now()
            with conn:
                for paper, content_hash in papers:
                    insert_paper(conn, paper, content_hash, now_ticks)
        finally:
            conn.close()
        os.replace(tmp_name, out_db)
    except BaseException:
        try:
            os.remove(tmp_name)
        except OSError:
            pass
        raise
    print(f"seed: {len(papers)} paper(s) from {exams_dir} -> {out_db}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
