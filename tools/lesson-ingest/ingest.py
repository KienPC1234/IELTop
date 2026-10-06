"""Turn the raw lesson folder into structured, standardized JSON per unit.

The input is a teaching pack: .docx homework with answer keys, .pptx slide
decks, .xlsx vocabulary tables, .pdf handouts and .mp3 listening audio. The
output keeps the teacher's original text intact while adding structured metadata:
- Clean module hierarchy (Units 1-10, Grammar Review, Writing Task 1, Writing Task 2, Midterm Review)
- Skill & Topic taxonomy for precise AI context retrieval and practice generation
- Bidirectional pairing between exercise sections and their corresponding answer keys
- Automatic filtering of junk drafts and duplicate files (e.g., 'Bản sao của', '(1)')

Run it from tools/lesson-ingest inside its conda env:

    python ingest.py --src "<Lesson Content>" --out ../../Content/Assets/Lessons --audio ../../Content/Assets/Audio
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
from dataclasses import dataclass, field
from pathlib import Path

UNIT_DIR_RE = re.compile(r"^unit\s*(\d+)$", re.IGNORECASE)
KEY_RE = re.compile(r"keys?$|answer|transcript", re.IGNORECASE)


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
    try:
        import docx
        from docx.table import Table
        from docx.text.paragraph import Paragraph
        from docx.oxml.text.paragraph import CT_P
        from docx.oxml.table import CT_Tbl
    except Exception as ex:
        report.warn(f"{path.name}: python-docx not available ({ex})")
        return None, 0

    try:
        document = docx.Document(str(path))
    except Exception as ex:
        report.warn(f"{path.name}: could not open docx ({ex})")
        return None, 0

    pictures = len(document.inline_shapes)
    blocks: list[dict] = []

    def add_text(text: str) -> None:
        text = text.strip()
        if not text:
            return
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
    except Exception as ex:
        report.warn(f"{path.name}: python-pptx not available ({ex})")
        return None, 0

    try:
        presentation = Presentation(str(path))
    except Exception as ex:
        report.warn(f"{path.name}: could not open pptx ({ex})")
        return None, 0

    slides: list[dict] = []
    pictures = 0
    for index, slide in enumerate(presentation.slides, start=1):
        lines: list[str] = []

        def shape_text(shape) -> None:
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
                try:
                    for child in shape.shapes:
                        shape_text(child)
                except Exception:
                    pass
                continue
            shape_text(shape)
        if lines:
            slides.append({"index": index, "text": "\n".join(lines)})

    report.files_read += 1
    return slides, pictures


# ---------------------------------------------------------------- xlsx

def read_xlsx(path: Path, report: Report) -> list[list[str]] | None:
    try:
        import openpyxl
    except Exception as ex:
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


# ---------------------------------------------------------------- vocab helpers

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


# ---------------------------------------------------------------- filtering & classification

def is_junk_file(path: Path, raw_root: Path) -> bool:
    """Detect draft duplicates, copies, and non-course files."""
    name = path.name.lower()
    rel = path.relative_to(raw_root).as_posix().lower()

    if "emotica ai" in rel:
        return True
    if name.startswith("bản sao của") or name.startswith("copy of"):
        return True
    if "(1)" in name:
        return True
    if "midterm review" in rel and name == "unit 9.pptx":
        return True
    return False


def classify_section(unit_slug: str, path: Path, raw_root: Path) -> tuple[str, str, bool]:
    """Returns (skill, topic, is_key) with high precision."""
    rel = path.relative_to(raw_root).as_posix().lower()
    name = path.stem.lower()
    suffix = path.suffix.lower()
    is_key = bool(KEY_RE.search(name))

    # Writing modules
    if unit_slug == "writing-grammar-review":
        skill = "Grammar"
        if "core sentence" in rel:
            topic = "Core Sentence Structure"
        elif "noun-phrase" in rel:
            topic = "Noun Phrases"
        elif "complex sentence" in rel:
            topic = "Complex Sentences"
        elif "relative clause" in rel:
            topic = "Relative Clauses"
        elif "comparative" in rel:
            topic = "Comparative Adjectives & Adverbs"
        else:
            topic = "Grammar Review Practice"
        return skill, topic, is_key

    if unit_slug == "writing-task-1":
        skill = "Writing"
        if "process" in rel:
            topic = "Process"
        elif "maps" in rel:
            topic = "Maps"
        elif "trends" in rel:
            topic = "Trends"
        elif "comparison" in rel:
            topic = "Comparison"
        elif "multiple chart" in rel:
            topic = "Multiple Charts"
        else:
            topic = "Task 1 Review"
        return skill, topic, is_key

    if unit_slug == "writing-task-2":
        skill = "Writing"
        if "opinion" in rel:
            topic = "Opinion Essay"
        elif "discussion" in rel:
            topic = "Discussion Essay"
        elif "pros cons" in rel or "pros-cons" in rel:
            topic = "Pros & Cons Essay"
        elif "problems" in rel:
            topic = "Problems & Solutions"
        elif "double question" in rel:
            topic = "Double Questions"
        else:
            topic = "Task 2 Review"
        return skill, topic, is_key

    # Standard Units (Unit 1 to 10)
    if suffix in (".xlsx", ".xls"):
        skill = "Vocabulary"
        topic = "Listening Vocabulary" if "listening" in name else "Reading Vocabulary"
        return skill, topic, False

    if "grammar" in name or "tense" in name or "relative clause" in name or "participle" in name:
        skill = "Grammar"
        if "tense" in name:
            topic = "Tenses & Aspect"
        elif "relative clause" in name:
            topic = "Relative Clauses"
        elif "participle" in name:
            topic = "Participles & Infinitives"
        elif "derivative" in name or "verb form" in name:
            topic = "Verb Forms & Derivatives"
        else:
            topic = "Grammar"
        return skill, topic, is_key

    if "reading" in name:
        skill = "Reading"
        topic = "Reading Comprehension"
        return skill, topic, is_key

    if "listening" in name or "transcript" in name:
        skill = "Listening"
        topic = "Listening Comprehension"
        return skill, topic, is_key

    if "speaking" in name:
        skill = "Speaking"
        topic = "Speaking Practice"
        return skill, topic, is_key

    # Task 1 handouts found inside Unit 5-8
    if "process" in rel:
        return "Writing", "Process", is_key
    if "maps" in rel:
        return "Writing", "Maps", is_key
    if "trends" in rel:
        return "Writing", "Trends", is_key
    if "comparison" in rel:
        return "Writing", "Comparison", is_key

    if suffix == ".pptx":
        return "Lecture Slide", "Unit Overview", False

    return "Lesson", "General", is_key


def pair_answer_keys(sections: list[dict]) -> None:
    """Pair each answer key section with its corresponding question section."""
    keys = [s for s in sections if s.get("isAnswerKey")]
    questions = [s for s in sections if not s.get("isAnswerKey")]

    for k in keys:
        norm_k = re.sub(r"keys?$|answer-keys?$|transcript", "", k["id"].replace("-", " ")).strip()
        best = None
        for q in questions:
            norm_q = q["id"].replace("-", " ").strip()
            if norm_k and norm_k in norm_q:
                best = q
                break
            if norm_q and norm_q in norm_k:
                best = q
                break

        if not best:
            for q in questions:
                if q["skill"] == k["skill"] and q["topic"] == k["topic"] and q["skill"] != "Vocabulary":
                    best = q
                    break

        if best:
            k["targetSectionId"] = best["id"]
            if not best.get("keySectionId"):
                best["keySectionId"] = k["id"]


# ---------------------------------------------------------------- unit planning

@dataclass
class UnitPlan:
    slug: str
    unit: str
    title: str
    category: str
    files: list[Path]


def plan_units(raw_root: Path, report: Report) -> list[UnitPlan]:
    plans: list[UnitPlan] = []

    # 1. Standard Units 1 to 10
    unit_dirs = sorted(
        [d for d in raw_root.iterdir() if d.is_dir() and UNIT_DIR_RE.match(d.name)],
        key=lambda d: int(UNIT_DIR_RE.match(d.name).group(1)),
    )

    for u_dir in unit_dirs:
        num = int(UNIT_DIR_RE.match(u_dir.name).group(1))
        slug = f"unit-{num}"
        files: list[Path] = []
        for p in sorted(u_dir.rglob("*")):
            if not p.is_file():
                continue
            if is_junk_file(p, raw_root):
                report.files_skipped.append(p.name)
                continue
            # Keep core unit files (subfolders 1. Process etc. are also mirrored in writing-task-1)
            files.append(p)

        plans.append(UnitPlan(
            slug=slug,
            unit=f"Unit {num}",
            title=f"Unit {num}",
            category="Core Units",
            files=files,
        ))

    # 2. Writing Module: split into 3 dedicated, high-value units
    writing_dir = raw_root / "Writing_"
    if writing_dir.is_dir():
        # 2a. Grammar Review
        gr_dir = writing_dir / "Grammar Review"
        if gr_dir.is_dir():
            gr_files = [p for p in sorted(gr_dir.rglob("*")) if p.is_file() and not is_junk_file(p, raw_root)]
            plans.append(UnitPlan(
                slug="writing-grammar-review",
                unit="Writing - Grammar Review",
                title="Writing - Grammar Foundation",
                category="Writing & Grammar",
                files=gr_files,
            ))

        # 2b. Task 1
        t1_dir = writing_dir / "Module 1 - Task 1"
        if t1_dir.is_dir():
            t1_files = [p for p in sorted(t1_dir.rglob("*")) if p.is_file() and not is_junk_file(p, raw_root)]
            # Add root Task 1 handouts if present
            for root_t1 in ("T1 Comparison - Lưu ý.docx", "T1 Review Practice.docx"):
                p = raw_root / root_t1
                if p.is_file() and p not in t1_files:
                    t1_files.append(p)
            plans.append(UnitPlan(
                slug="writing-task-1",
                unit="Writing Task 1",
                title="Writing Task 1 - Academic Reports",
                category="Writing",
                files=t1_files,
            ))

        # 2c. Task 2
        t2_dir = writing_dir / "Module 2 - Task 2"
        if t2_dir.is_dir():
            t2_files = [p for p in sorted(t2_dir.rglob("*")) if p.is_file() and not is_junk_file(p, raw_root)]
            plans.append(UnitPlan(
                slug="writing-task-2",
                unit="Writing Task 2",
                title="Writing Task 2 - Essay Types",
                category="Writing",
                files=t2_files,
            ))

    # 3. Midterm Review
    midterm_dir = raw_root / "Midterm Review"
    if midterm_dir.is_dir():
        mt_files = [p for p in sorted(midterm_dir.rglob("*")) if p.is_file() and not is_junk_file(p, raw_root)]
        if mt_files:
            plans.append(UnitPlan(
                slug="midterm-review",
                unit="Midterm Review",
                title="Midterm Review (Units 1-5)",
                category="Review",
                files=mt_files,
            ))

    return plans


# ---------------------------------------------------------------- ingest unit

def ingest_plan(plan: UnitPlan, raw_root: Path, out_dir: Path, report: Report, audio_out: Path | None, embedder = None) -> None:
    sections: list[dict] = []
    vocabulary: list[dict] = []
    decks: list[dict] = []
    audio: list[str] = []
    source_names: list[str] = []
    pictures_skipped = 0

    seen_stems: set[str] = set()

    for path in plan.files:
        rel = path.relative_to(raw_root).as_posix()
        suffix = path.suffix.lower()
        name = path.name
        source_names.append(rel)

        skill, topic, is_key = classify_section(plan.slug, path, raw_root)
        stem_slug = re.sub(r"[^a-z0-9]+", "-", path.stem.lower()).strip("-")
        if stem_slug in seen_stems:
            stem_slug = f"{stem_slug}-{len(seen_stems)}"
        seen_stems.add(stem_slug)

        if suffix in (".docx", ".doc"):
            blocks, pictures = read_docx(path, report)
            pictures_skipped += pictures
            if blocks:
                sections.append({
                    "id": stem_slug,
                    "skill": skill,
                    "topic": topic,
                    "title": path.stem,
                    "isAnswerKey": is_key,
                    "keySectionId": "",
                    "targetSectionId": "",
                    "blocks": blocks,
                })
        elif suffix == ".pptx":
            slides, pictures = read_pptx(path, report)
            pictures_skipped += pictures
            if slides:
                decks.append({
                    "file": name,
                    "topic": topic or "Slide Presentation",
                    "slides": slides,
                })
        elif suffix in (".xlsx", ".xls"):
            rows = read_xlsx(path, report)
            if rows:
                vocab_list = vocab_from_rows(rows, report)
                vocabulary.extend(vocab_list)
                sections.append({
                    "id": stem_slug,
                    "skill": "Vocabulary",
                    "topic": topic or "Vocabulary",
                    "title": path.stem,
                    "isAnswerKey": False,
                    "keySectionId": "",
                    "targetSectionId": "",
                    "blocks": [{"type": "table", "rows": rows}],
                })
        elif suffix == ".pdf":
            text = read_pdf(path, report)
            if text:
                sections.append({
                    "id": stem_slug,
                    "skill": skill,
                    "topic": topic,
                    "title": path.stem,
                    "isAnswerKey": is_key,
                    "keySectionId": "",
                    "targetSectionId": "",
                    "blocks": [{"type": "text", "text": text}],
                })
        elif suffix in (".mp3", ".wav", ".m4a"):
            target_rel = f"Lessons/{plan.slug}/{path.name}"
            audio.append(target_rel)
            if audio_out is not None:
                destination = audio_out / "Lessons" / plan.slug / path.name
                destination.parent.mkdir(parents=True, exist_ok=True)
    # Inject full external reading passages from consolidated_readings.json if available
    consolidated_path = Path(__file__).resolve().parent.parent / "study4-scraper" / "consolidated_readings.json"
    if consolidated_path.exists():
        try:
            with open(consolidated_path, "r", encoding="utf-8") as f:
                all_readings = json.load(f)
            unit_readings = all_readings.get(plan.slug, [])
            for r in unit_readings:
                r_id = f"reading-{r['id']}"
                key_id = f"key-reading-{r['id']}" if r.get("answers") else ""
                blocks = []
                paras = r.get("paragraphs", [])
                if paras:
                    blocks.append({
                        "type": "text",
                        "text": f"Source: {r.get('cambridgeRef', '')} | {r.get('source', '')}\n\n" + "\n\n".join(paras)
                    })
                questions = r.get("questions", [])
                if questions:
                    blocks.append({
                        "type": "text",
                        "text": "EXERCISE QUESTIONS:\n\n" + "\n\n".join(questions)
                    })
                if blocks:
                    sections.append({
                        "id": r_id,
                        "skill": "Reading",
                        "topic": r.get("title", "Reading Passage"),
                        "title": f"Passage: {r.get('title', '')} ({r.get('cambridgeRef', '')})",
                        "isAnswerKey": False,
                        "keySectionId": key_id,
                        "targetSectionId": "",
                        "blocks": blocks,
                    })

                answers = r.get("answers", [])
                if answers:
                    sections.append({
                        "id": key_id,
                        "skill": "Reading",
                        "topic": r.get("title", "Reading Passage"),
                        "title": f"Answer Key: {r.get('title', '')}",
                        "isAnswerKey": True,
                        "keySectionId": "",
                        "targetSectionId": r_id,
                        "blocks": [
                            {"type": "text", "text": "ANSWER KEY & EXPLANATIONS:\n\n" + "\n".join(answers)}
                        ],
                    })
        except Exception as ex:
            report.warn(f"Failed loading consolidated readings for {plan.slug}: {ex}")

    pair_answer_keys(sections)

    # Pre-compute Vector Embeddings for Hybrid/Semantic Search (FastEmbed bge-small-en-v1.5)
    if embedder is not None:
        if sections:
            section_texts = []
            for s in sections:
                flat_text = ""
                for b in s.get("blocks", []):
                    if b.get("type") == "text":
                        flat_text += " " + b.get("text", "")
                    elif b.get("type") == "table":
                        for row in b.get("rows", []):
                            flat_text += " " + " ".join(row)
                summary = f"{s.get('title', '')}. Skill: {s.get('skill', '')}. Topic: {s.get('topic', '')}. {flat_text.strip()[:1400]}"
                section_texts.append(summary)

            try:
                embs = list(embedder.embed(section_texts))
                for s, emb in zip(sections, embs):
                    s["embedding"] = [round(float(x), 5) for x in emb]
            except Exception as ex:
                report.warn(f"{plan.slug}: section embedding error ({ex})")

        if decks:
            deck_texts = [
                f"Slides: {d.get('file', '')}. " + " ".join(sl.get("text", "") for sl in d.get("slides", []))[:1400]
                for d in decks
            ]
            try:
                deck_embs = list(embedder.embed(deck_texts))
                for d, emb in zip(decks, deck_embs):
                    d["embedding"] = [round(float(x), 5) for x in emb]
            except Exception:
                pass

    distinct_topics = sorted({s["topic"] for s in sections if s.get("topic")})
    distinct_skills = sorted({s["skill"] for s in sections if s.get("skill")})

    unit_data = {
        "unit": plan.unit,
        "title": plan.title,
        "slug": plan.slug,
        "category": plan.category,
        "topics": distinct_topics,
        "skills": distinct_skills,
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
    out_path = out_dir / f"{plan.slug}.json"
    out_path.write_text(json.dumps(unit_data, ensure_ascii=False, indent=2), encoding="utf-8")

    report.units += 1
    print(f"  [{plan.category}] {plan.title} ({plan.slug}): {len(sections)} sections, "
          f"{len(vocabulary)} words, {len(decks)} decks, {len(audio)} audio")


# ---------------------------------------------------------------- main

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
        print(f"Source folder not found: {src}", file=sys.stderr)
        return 1

    report = Report()
    print(f"Standardizing lessons from {src}")

    embedder = None
    try:
        from fastembed import TextEmbedding
        print("Initializing FastEmbed (bge-small-en-v1.5) for pre-computed vector embeddings...")
        embedder = TextEmbedding()
        print("Embedding model ready.")
    except Exception as ex:
        print(f"FastEmbed not available ({ex}); proceeding without vector embeddings.")

    plans = plan_units(src, report)

    for plan in plans:
        try:
            ingest_plan(plan, src, out, report, audio_out, embedder)
        except Exception as ex:
            report.warn(f"{plan.slug}: failed ({ex})")

    report_path = Path(args.report) if args.report else out / "ingest-report.md"
    lines = [
        "# Lesson Ingest & Standardization Report",
        "",
        f"- Units written: {report.units}",
        f"- Files read: {report.files_read}",
        f"- Junk/duplicate files skipped: {len(report.files_skipped)}",
        f"- Vocabulary rows parsed: {report.vocab_rows}",
        f"- Warnings: {len(report.warnings)}",
        "",
        "## Skipped Files (Duplicates / Drafts / Junk)",
        "",
    ]
    lines.extend(f"- {f}" for f in sorted(set(report.files_skipped)))
    lines.append("")

    if report.warnings:
        lines.append("## Warnings")
        lines.append("")
        lines.extend(f"- {w}" for w in report.warnings)
        lines.append("")

    report_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"\nIngest completed: {report.units} unit file(s) written to {out}")
    print(f"Report: {report_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
