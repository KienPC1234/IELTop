"""Check that every symbol in phoneme-map.json exists in the model label set.

This catches the common failure where the map uses one IPA convention and the
model uses another, which would make every pronunciation score wrong.
"""
import json
import sys
from pathlib import Path

labels_path = Path("models/mdd-labels.json")
map_path = Path("../../Content/Assets/Models/phoneme-map.json")

labels = json.loads(labels_path.read_text(encoding="utf-8"))
special = {"<pad>", "<unk>", "<s>", "</s>", "|"}
model_symbols = set()
for t in labels:
    if t in special:
        continue
    # strip stress marks the C# decoder removes
    model_symbols.add(t.replace("\u02c8", "").replace("\u02cc", ""))

phoneme_map = json.loads(map_path.read_text(encoding="utf-8"))

missing = {}
for word, phones in phoneme_map.items():
    if word.startswith("_"):
        continue
    bad = [p for p in phones if p not in model_symbols]
    if bad:
        missing[word] = bad

if not missing:
    print("OK: every symbol in phoneme-map.json exists in the model labels.")
else:
    print(f"MISMATCH in {len(missing)} word(s):")
    for word, bad in sorted(missing.items()):
        printable = " ".join(f"{p} (U+{ord(c):04X})" for p in bad for c in [p[0]])
        print(f"  {word}: {bad}")

print(f"\nmodel symbols ({len(model_symbols)}):")
sys.stdout.buffer.write((" ".join(sorted(model_symbols)) + "\n").encode("utf-8"))
