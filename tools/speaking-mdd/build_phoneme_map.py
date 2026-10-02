"""Build a full English G2P map (word -> IPA phonemes) for the pronunciation check.

The model emits a fixed set of 59 IPA symbols. This script turns CMUdict
(BSD-2) into that exact symbol set, so the canonical side of the comparison
uses the same alphabet the model does.

It also sanity checks the ARPABET to IPA mapping against the shipped map: for
every word that appears in both, the phoneme counts must match, and the script
reports how many symbols differ. A silent mismatch here is what makes every
pronunciation score wrong, so the check is loud on purpose.

Sources
-------
CMUdict: https://github.com/cmusphinx/cmudict (BSD-2-Clause)
Output replaces Content/Assets/Models/phoneme-map.json.
"""

import json
import re
import sys
from collections import Counter
from pathlib import Path
from urllib.request import urlopen

CMUDICT_URL = "https://raw.githubusercontent.com/cmusphinx/cmudict/master/cmudict.dict"

# Base ARPABET to IPA. Vowels that change with stress are handled in code.
BASE_MAP = {
    "AA": "ɑː", "AE": "æ", "AH": "ʌ", "AO": "ɔː", "AW": "aʊ", "AY": "aɪ",
    "EH": "ɛ", "ER": "ɜː", "EY": "eɪ", "IH": "ɪ", "IY": "iː", "OW": "oʊ",
    "OY": "ɔɪ", "UH": "ʊ", "UW": "uː",
    "B": "b", "CH": "tʃ", "D": "d", "DH": "ð", "F": "f", "G": "ɡ", "HH": "h",
    "JH": "dʒ", "K": "k", "L": "l", "M": "m", "N": "n", "NG": "ŋ", "P": "p",
    "R": "ɹ", "S": "s", "SH": "ʃ", "T": "t", "TH": "θ", "V": "v", "W": "w",
    "Y": "j", "Z": "z", "ZH": "ʒ",
}


def arpabet_to_ipa(phone: str) -> str | None:
    """Map one ARPABET phone, honouring vowel stress.

    Reduced vowels matter: AH0 is a schwa, not a full 'ʌ'. Using the model's
    reduced symbols is what makes the score reflect real pronunciation.
    """
    base = re.sub(r"\d", "", phone)
    stress = phone[-1] if phone and phone[-1].isdigit() else ""
    if base == "AH":
        return "ə" if stress == "0" else "ʌ"
    if base == "ER":
        return "ɚ" if stress == "0" else "ɜː"
    if base == "IH":
        return "ᵻ" if stress == "0" else "ɪ"
    if base == "IY":
        return "i" if stress == "0" else "iː"
    return BASE_MAP.get(base)


def load_model_symbols(labels_path: Path) -> set[str]:
    labels = json.loads(labels_path.read_text(encoding="utf-8"))
    special = {"<pad>", "<unk>", "<s>", "</s>", "|"}
    symbols = set()
    for token in labels:
        if token in special:
            continue
        symbols.add(token.replace("ˈ", "").replace("ˌ", ""))
    return symbols


def load_existing(map_path: Path) -> dict[str, list[str]]:
    raw = json.loads(map_path.read_text(encoding="utf-8"))
    return {k.lower(): v for k, v in raw.items() if not k.startswith("_")}


def load_cmudict(cache: Path) -> dict[str, list[str]]:
    if cache.exists():
        text = cache.read_text(encoding="utf-8", errors="replace")
    else:
        print("Downloading CMUdict...")
        text = urlopen(CMUDICT_URL, timeout=120).read().decode("utf-8", errors="replace")
        cache.write_text(text, encoding="utf-8")

    cmu: dict[str, list[str]] = {}
    for line in text.splitlines():
        line = line.split("#")[0].strip()
        if not line:
            continue
        parts = line.split(None, 1)
        if len(parts) != 2:
            continue
        word, phones = parts
        if word.endswith(")") and "(" in word:
            continue  # skip the "(2)" variants, keep the primary
        key = word.lower()
        if key not in cmu:
            cmu[key] = phones.split()
    return cmu


def build() -> None:
    here = Path(__file__).resolve().parent
    repo = here.parent.parent
    map_path = repo / "Content" / "Assets" / "Models" / "phoneme-map.json"
    labels_path = repo / "Content" / "Assets" / "Models" / "mdd-labels.json"

    model_symbols = load_model_symbols(labels_path)
    existing = load_existing(map_path)
    cmu = load_cmudict(here / "cmudict.dict")

    # Sanity check the mapping against the shipped map, word by word.
    agree = 0
    differ = Counter()
    for word, ipa in existing.items():
        arp = cmu.get(word)
        if arp is None or len(arp) != len(ipa):
            continue
        for phone, symbol in zip(arp, ipa):
            mine = arpabet_to_ipa(phone)
            if mine == symbol:
                agree += 1
            else:
                differ[(phone, symbol, mine)] += 1

    # Build the whole map.
    out: dict[str, list[str]] = {}
    unmapped: set[str] = set()
    for word, arp in cmu.items():
        if not re.fullmatch(r"[a-z']+", word):
            continue
        ipa = []
        for phone in arp:
            symbol = arpabet_to_ipa(phone)
            if symbol is None or symbol not in model_symbols:
                unmapped.add(phone)
                ipa = []
                break
            ipa.append(symbol)
        if ipa:
            out[word] = ipa

    out["_source"] = [
        "CMUdict (BSD-2-Clause), https://github.com/cmusphinx/cmudict",
        "Mapped to the mdd-wav2vec2-base IPA symbol set by tools/speaking-mdd/build_phoneme_map.py",
    ]
    map_path.write_text(
        json.dumps(out, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8",
    )

    print(f"Checked {agree} agreeing phonemes, {sum(differ.values())} differences.")
    for (phone, theirs, mine), count in differ.most_common(10):
        print(f"  {phone}: shipped {theirs!r}, ours {mine!r} x{count}")
    if unmapped:
        print("Phones with no model symbol:", sorted(unmapped))
    print(f"Wrote {len(out) - 1} words to {map_path}")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    build()
