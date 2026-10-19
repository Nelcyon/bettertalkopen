"""Regenerates src/Aac.Core/Prediction/words-en.txt from the wordfreq package.

Usage: pip install wordfreq==3.1.1 && python tools/wordlist/make_wordlist.py
Output lines: "<word>\t<zipf*100>" sorted by frequency. Zipf = log10(occurrences per billion words).
Word data: wordfreq (https://github.com/rspeer/wordfreq), data licensed CC BY-SA 4.0.
"""
import pathlib
import re

import wordfreq

TOP_N = 60000
# Slurs are never offered as predictions; the user can still type any word letter by letter.
BLOCKED = {"nigger", "niggers", "nigga", "niggas", "faggot", "faggots", "fag", "fags", "retard", "retards",
           "retarded", "tranny", "trannies", "spic", "spics", "chink", "chinks", "kike", "kikes", "wetback"}
WORD = re.compile(r"^[a-z]+(?:'[a-z]+)?$")

out = pathlib.Path(__file__).resolve().parents[2] / "src/Aac.Core/Prediction/words-en.txt"
lines = []
for w in wordfreq.top_n_list("en", TOP_N):
    if not WORD.match(w) or w in BLOCKED:
        continue
    if len(w) == 1 and w not in ("a", "i"):
        continue
    lines.append(f"{w}\t{round(wordfreq.zipf_frequency(w, 'en') * 100)}")
out.write_text("\n".join(lines) + "\n", encoding="utf-8")
print(f"{len(lines)} words -> {out}")
