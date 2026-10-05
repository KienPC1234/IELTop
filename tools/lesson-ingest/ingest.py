"""Turn the raw lesson folder into one normalized JSON per unit.

The input is a teaching pack: .docx homework with answer keys, .pptx slide
decks, .xlsx vocabulary tables, .pdf handouts and .mp3 listening audio. The
output keeps the words exactly as written (no rewriting, no summary) and only
adds structure, so nothing the teacher wrote is lost. Anything that cannot be
read is recorded in the report instead of being dropped silently.

Run it from tools/lesson-ingest inside its conda env:

    python ingest.py --src "<Lesson Content>" --out ../../Content/Assets/Lessons
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
from dataclasses import dataclass, field
from pathlib import Path

# python-docx / python-pptx / openpyxl are imported lazily inside the readers
# so a missing optional reader only disables that file type, not the whole run.


UNIT_RE = re.compile(r"^unit\s*(\d+)$", re.IGNORECASE)
SKILL_RE = re.compile(r"(grammar|listening|reading|speaking|writing)", re.IGNORECASE)
KEY_RE = re.compile(r"keys?$|answer", re.IGNORECASE)


@dataclass
class Report:
    units: int = 0
    files_read: int = 0
    files_skipped: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    vocab_rows: int = 0

    def warn(self, message: str) -> None:
        self.warnings.append(message)


# ---------------------------------------------------------------- docx

def read_docx(path: Path, report: Report) -> tuple[list[dict] | None, int]:
    """Paragraphs and tables in the order they appear in the document.

    Returns the blocks plus the number of inline pictures. Pictures (charts,
    photos, scanned pages) carry no readable text without OCR, which the app
    deliberately does not do, so they are counted and reported instead of
    silently lost.
    """
    try:
        import docx  # python-docx
        from docx.document import Document as DocxDocument
        from docx.table import Table
        from docx.text.paragraph import Paragraph
        from docx.oxml.text.paragraph import CT_P
        from docx.oxml.table import CT_Tbl
    except Exception as ex:  # pragma: no cover - env dependent
        report.warn(f"{path.name}: python-docx not available ({ex})")
        return None, 0

    try:
        document = docx.Document(str(path))
    except Exception as ex:
        report.warn(f"{path.name}: could not open docx ({ex})")
        return None, 0

    pictures = len(document.inline_shapes)
    if pictures:
        report.warn(f"{path.name}: {pictures} picture(s) skipped (no text to read without OCR)")

    blocks: list[dict] = []

    def add_text(text: str) -> None:
        text = text.strip()
        if not text:
            return
        # Merge into the previous text block so a run of lines stays one block.
        if blocks and blocks[-1]["type"] == "text":
            blocks[-1]["text"] += "\n" + text
        else:
            blocks.append({"type": "text", "text": text})

    def walk(parent) -> None:
        for child in parent.element.body.iterchildren():
            if isinstance(child, CT_P):
                add_text(Paragraph(child, parent).text)
            elif isinstance(child, CT_Tbl):
                table = Table(child, parent)
                rows = [
                    [cell.text.strip() for cell in row.cells]
                    for row in table.rows
                ]
                # Drop rows that are entirely empty.
                rows = [r for r in rows if any(c for c in r)]
                if rows:
                    blocks.append({"type": "table", "rows": rows})

    try:
        walk(document)
    except Exception as ex:
        report.warn(f"{path.name}: partial read ({ex})")

    report.files_read += 1
    return blocks, pictures


# ---------------------------------------------------------------- pptx

def read_pptx(path: Path, report: Report) -> tuple[list[dict] | None, int]:
    try:
        from pptx import Presentation
    except Exception as ex:  # pragma: no cover - env dependent
        report.warn(f"{path.name}: python-pptx not available ({ex})")
        return None, 0

    try:
        presentation = Presentation(str(path))
    except Exception as ex:
        report.warn(f"{path.name}: could not open pptx ({ex})")
        return None, 0

    slides: list[dict] = []
    pictures = 0
    tables = 0
    for index, slide in enumerate(presentation.slides, start=1):
        lines: list[str] = []

        def shape_text(shape) -> None:
            # Tables hold real content (answer grids, word lists): keep the
            # cells as text instead of dropping the whole shape.
            if getattr(shape, "has_table", False):
                nonlocal_tables = []
                for row in shape.table.rows:
                    cells = [cell.text.strip().replace("\n", " ") for cell in row.cells]
                    if any(cells):
                        nonlocal_tables.append(" | ".join(cells))
                if nonlocal_tables:
                    lines.append("[table] " + " / ".join(nonlocal_tables))
                return
            if not getattr(shape, "has_text_frame", False):
                return
            for paragraph in shape.text_frame.paragraphs:
                text = "".join(run.text for run in paragraph.runs).strip()
                if text:
                    lines.append(text)

        for shape in slide.shapes:
            shape_type = str(getattr(shape, "shape_type", ""))
            if "PICTURE" in shape_type:
                pictures += 1
                continue
            if "GROUP" in shape_type:
                # Grouped boxes carry text (labels on diagrams); walk inside.
                try:
                    for child in shape.shapes:
                        shape_text(child)
                except Exception:
                    pass
                continue
            if getattr(shape, "has_table", False):
                tables += 1
            shape_text(shape)
        if lines:
            slides.append({"index": index, "text": "\n".join(lines)})

    if pictures:
        report.warn(f"{path.name}: {pictures} picture(s) skipped (charts and photos have no readable text)")
    report.files_read += 1
    return slides, pictures


# ---------------------------------------------------------------- xlsx

def read_xlsx(path: Path, report: Report) -> list[list[str]] | None:
    try:
        import openpyxl
    except Exception as ex:  # pragma: no cover - env dependent
        report.warn(f"{path.name}: openpyxl not available ({ex})")
        return None

    try:
        workbook = openpyxl.load_workbook(str(path), read_only=True, data_only=True)
    except Exception as ex:
        report.warn(f"{path.name}: could not open xlsx ({ex})")
        return None

    rows: list[list[str]] = []
    for sheet in workbook.worksheets:
        for row in sheet.iter_rows(values_only=True):
            cells = ["" if c is None else str(c).strip() for c in row]
            if any(cells):
                rows.append(cells)
    workbook.close()
    report.files_read += 1
    return rows


# ---------------------------------------------------------------- pdf

def read_pdf(path: Path, report: Report) -> str | None:
    try:
        from pypdf import PdfReader
    except Exception:
        report.warn(f"{path.name}: PDF text skipped (pypdf not installed)")
        return None

    try:
        reader = PdfReader(str(path))
        parts = [(page.extract_text() or "") for page in reader.pages]
    except Exception as ex:
        report.warn(f"{path.name}: could not read pdf ({ex})")
        return None

    report.files_read += 1
    return "\n".join(p for p in parts if p.strip()).strip()


# ---------------------------------------------------------------- helpers

def _is_vocab_header(row: list[str]) -> bool:
    cells = [c for c in row if c]
    if len(cells) < 2:
        return False
    return row[0].strip().lower() in ("word", "word/phrase", "word phrase")


def _header_columns(row: list[str]) -> dict:
    header = [c.lower() for c in row]

    def col(*names: str) -> int:
        for j, cell in enumerate(header):
            if any(n in cell for n in names):
                return j
        return -1

    return {
        "word": 0,
        "form": col("form", "word form", "word?form"),
        "meaning": col("meaning", "nghia", "nghĩa"),
        "example": col("example from", "example from text", "example from passage"),
        "extraExample": col("common", "extra example"),
        "ipa": col("ipa"),
        "derivatives": col("derivativ", "derivative"),
    }


def vocab_from_rows(rows: list[list[str]], report: Report) -> list[dict]:
    """Read the vocabulary rows, which live in several header/body sections.

    Each section repeats its own header, sometimes with different column names
    (for example "Vietnamese Meaning" then just "Example from Passage"). A row
    with one cell is a heading such as a passage title and is kept as the group
    for the words below it, not read as a word.
    """
    out: list[dict] = []
    columns: dict | None = None
    group = ""

    for row in rows:
        if _is_vocab_header(row):
            columns = _header_columns(row)
            continue
        if columns is None:
            continue

        filled = [c for c in row if c]
        if len(filled) == 1:
            # A heading, for example "Passage 3: How Bad Is Ocean Garbage, Really?".
            group = filled[0].strip()
            continue
        if not filled:
            continue

        def at(index: int) -> str:
            return row[index].strip() if 0 <= index < len(row) else ""

        word = at(columns["word"]).strip()
        if not word or word.lower() in ("word", "word/phrase", "word phrase"):
            continue

        out.append({
            "word": word,
            "group": group,
            "form": at(columns["form"]),
            "meaning": at(columns["meaning"]),
            "example": at(columns["example"]),
            "extraExample": at(columns["extraExample"]),
            "ipa": at(columns["ipa"]),
            "derivatives": at(columns["derivatives"]),
        })

    report.vocab_rows += len(out)
    return out


def guess_skill(name: str) -> str:
    match = SKILL_RE.search(name)
    if match:
        return match.group(1).capitalize()
    return "Lesson"


def sorted_units(src: Path) -> list[Path]:
    dirs = [d for d in src.iterdir() if d.is_dir()]

    def order(d: Path) -> tuple:
        match = UNIT_RE.match(d.name)
        if match:
            return (0, int(match.group(1)))
        return (1, d.name.lower())

    return sorted(dirs, key=order)


def ingest_unit(unit_dir: Path, out_dir: Path, report: Report, audio_out: Path | None) -> None:
    files = sorted(
        (p for p in unit_dir.rglob("*") if p.is_file()),
        key=lambda p: str(p).lower(),
    )

    sections: list[dict] = []
    vocabulary: list[dict] = []
    decks: list[dict] = []
    audio: list[str] = []
    source_names: list[str] = []
    pictures_skipped = 0
    slug = re.sub(r"[^a-z0-9]+", "-", unit_dir.name.lower()).strip("-")

    for path in files:
        rel = path.relative_to(unit_dir).as_posix()
        suffix = path.suffix.lower()
        name = path.name
        source_names.append(rel)

        if suffix in (".docx", ".doc"):
            blocks, pictures = read_docx(path, report)
            pictures_skipped += pictures
            if blocks:
                is_key = bool(KEY_RE.search(path.stem))
                sections.append({
                    "id": re.sub(r"[^a-z0-9]+", "-", path.stem.lower()).strip("-"),
                    "skill": guess_skill(name),
                    "title": path.stem,
                    "isAnswerKey": is_key,
                    "blocks": blocks,
                })
        elif suffix == ".pptx":
            slides, pictures = read_pptx(path, report)
            pictures_skipped += pictures
            if slides:
                decks.append({"file": rel, "slides": slides})
        elif suffix in (".xlsx", ".xls"):
            rows = read_xlsx(path, report)
            if rows:
                vocabulary.extend(vocab_from_rows(rows, report))
                sections.append({
                    "id": re.sub(r"[^a-z0-9]+", "-", path.stem.lower()).strip("-"),
                    "skill": "Vocabulary",
                    "title": path.stem,
                    "isAnswerKey": False,
                    "blocks": [{"type": "table", "rows": rows}],
                })
        elif suffix == ".pdf":
            text = read_pdf(path, report)
            if text:
                sections.append({
                    "id": re.sub(r"[^a-z0-9]+", "-", path.stem.lower()).strip("-"),
                    "skill": guess_skill(name),
                    "title": path.stem,
                    "isAnswerKey": bool(KEY_RE.search(path.stem)),
                    "blocks": [{"type": "text", "text": text}],
                })
        elif suffix in (".mp3", ".wav", ".m4a"):
            # Audio is listed by a stable relative path under the audio folder,
            # and copied there when --audio is given so the app can serve it
            # without the teacher's folder layout. The copy is gitignored.
            target_rel = f"Lessons/{slug}/{path.name}"
            audio.append(target_rel)
            if audio_out is not None:
                destination = audio_out / "Lessons" / slug / path.name
                destination.parent.mkdir(parents=True, exist_ok=True)
                if not destination.exists() or destination.stat().st_size != path.stat().st_size:
                    shutil.copy2(path, destination)
        # images and other files are listed in sourceNames only

    unit = {
        "unit": unit_dir.name,
        "title": unit_dir.name,
        "source": (
            "IELTop lesson content. Used and redistributed with the owner's "
            "permission. Original teaching material from the IELTop course."
        ),
        "files": source_names,
        "audio": audio,
        "sections": sections,
        "vocabulary": vocabulary,
        "slides": decks,
        "picturesSkipped": pictures_skipped,
    }

    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / f"{slug}.json"
    out_path.write_text(json.dumps(unit, ensure_ascii=False, indent=2), encoding="utf-8")

    report.units += 1
    print(f"  {unit_dir.name}: {len(sections)} section(s), "
          f"{len(vocabulary)} word(s), {len(decks)} deck(s), {len(audio)} audio")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--src", required=True, help="Lesson Content folder")
    parser.add_argument("--out", required=True, help="Output folder for unit JSON")
    parser.add_argument("--audio", default=None, help="Copy audio here (e.g. Content/Assets/Audio)")
    parser.add_argument("--report", default=None, help="Report path (default: <out>/ingest-report.md)")
    args = parser.parse_args()

    src = Path(args.src).expanduser().resolve()
    out = Path(args.out).expanduser().resolve()
    audio_out = Path(args.audio).expanduser().resolve() if args.audio else None
    if not src.is_dir():
        print(f"source folder not found: {src}", file=sys.stderr)
        return 1

    report = Report()
    print(f"Reading {src}")
    for unit_dir in sorted_units(src):
        try:
            ingest_unit(unit_dir, out, report, audio_out)
        except Exception as ex:
            report.warn(f"{unit_dir.name}: failed ({ex})")

    report_path = Path(args.report) if args.report else out / "ingest-report.md"
    lines = [
        "# Lesson ingest report",
        "",
        f"- Units written: {report.units}",
        f"- Files read: {report.files_read}",
        f"- Vocabulary rows: {report.vocab_rows}",
        f"- Warnings: {len(report.warnings)}",
        "",
    ]
    if report.warnings:
        lines.append("## Warnings")
        lines.append("")
        lines.extend(f"- {w}" for w in report.warnings)
        lines.append("")
    report_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"\nWrote {report.units} unit file(s) to {out}")
    print(f"Report: {report_path}")
    if report.warnings:
        print(f"{len(report.warnings)} warning(s) - see the report")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
