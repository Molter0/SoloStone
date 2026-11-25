#!/usr/bin/env python3
"""Generate facts/classic-2014.json: the Classic-format cards (set 1646, VAN_*) of the 37.0 client.

Input: HearthSim hsdata CardDefs.xml at tag 254477 (client 37.0.0.254477), the same file facts/ already pins for
locale text (its SHA-256 is PINNED below; any other file is refused). The file is not part of this repository.

    python3 tools/gen_classic_facts.py --hsdata <path to CardDefs.xml>          # write the file
    python3 tools/gen_classic_facts.py --hsdata <path to CardDefs.xml> --check  # fail if the file is stale

Rows use the facts schema (facts/README.md) as overlay rows. A row whose original card is in the Violet Hold file
carries originalId/originalDbf (its script comes from there). Encoding rules, each covered by tests/facts-drift:
- a weapon's durability (tag 45 in hsdata) is written as tag 187, as the overlay generator does;
- Spell Damage (192) keeps the original row's amount: hsdata stores it as a flag (Overload amounts are real);
- Elusive (1211) also writes the engine's two tags, can't be targeted by spells (311) and by Hero Powers (332);
- "Destroy any minion damaged by this minion" also writes Poisonous (363), and an Enrage card keeps Enraged (212),
  both taken from the original row, which the engine scripts read;
- no locale file: the client ships the same VAN text in every language, so the plugin leaves set 1646 text to it;
- Story_* puzzle rows and *_puzzle rows are left out, and so is an id the Violet Hold file already holds.
"""
import argparse
import hashlib
import json
import os
import sys
import xml.etree.ElementTree as ET

PINNED = "de717fc210614be2033152cffae6052fb1cd6b85812dc598218ce75263ca9df3"
BUILD = "37.0.0.254477"
META = "classic-2014"
VANILLA = 1646
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = os.path.join(ROOT, "facts", "violet-hold-2026-09.json")
OUT = os.path.join(ROOT, "facts", META + ".json")
KEPT_FROM_ORIGINAL = (192,)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def text_hash(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()[:16]


def read_vanilla(path):
    out = []
    for _, el in ET.iterparse(path):
        if el.tag != "Entity":
            continue
        tags = el.findall("Tag")
        card_set = next((t.get("value") for t in tags if t.get("enumID") == "183"), None)
        cid = el.get("CardID")
        if card_set == str(VANILLA) and cid.startswith("VAN_") and "puzzle" not in cid.lower():
            row = {"id": cid, "dbf": int(el.get("ID")), "tags": [], "refs": [], "loc": {}, "entourage": []}
            for t in tags:
                n = int(t.get("enumID"))
                if t.get("type") == "LocString":
                    if n in (184, 185):
                        row["loc"][n] = {child.tag: child.text or "" for child in t}
                    continue
                if t.get("type") == "String" or t.get("value") is None:
                    continue
                row["tags"].append([n, int(t.get("value"))])
            row["refs"] = [[int(t.get("enumID")), int(t.get("value"))] for t in el.findall("ReferencedTag") if t.get("value") is not None]
            row["entourage"] = [e.get("cardID") for e in el.findall("EntourageCard")]
            out.append(row)
        el.clear()
    return out


def base_rows():
    with open(BASE, encoding="utf-8") as f:
        doc = json.load(f)
    rows = {}
    for r in doc["cards"]:
        rows.setdefault(r["id"], r)
    for r in doc["overlay"]["cards"]:
        if not r.get("snapshot"):
            rows[r["id"]] = r
    by_dbf = {}
    for r in doc["cards"] + doc["overlay"]["cards"]:
        by_dbf.setdefault(r["dbf"], r["id"])
    return rows, by_dbf


def build(hsdata):
    rows, by_dbf = base_rows()
    cards = []
    for v in read_vanilla(hsdata):
        if v["id"] in rows:
            continue  # VAN_EX1_572 is already a Violet Hold overlay row; the engine gives it Ysera's behaviour.
        tags = dict((n, val) for n, val in v["tags"])
        original = rows.get(v["id"][4:])
        if original is None and tags.get(858) in by_dbf and by_dbf[tags[858]] in rows:
            original = rows[by_dbf[tags[858]]]
        otags = dict((n, val) for n, val in original["tags"]) if original else {}
        if tags.get(202) == 7 and 45 in tags:
            tags[187] = tags.pop(45)
        for n in KEPT_FROM_ORIGINAL:
            if n in tags and n in otags:
                tags[n] = otags[n]
        if tags.get(1211):
            tags[311] = 1
            tags[332] = 1
        name = v["loc"].get(185, {}).get("enUS", "")
        text = v["loc"].get(184, {}).get("enUS", "")
        if "Destroy any minion damaged by this minion" in text and otags.get(363):
            tags[363] = otags[363]
        if "Enrage" in text and otags.get(212):
            tags[212] = otags[212]
        tags.setdefault(321, 0)
        row = {"id": v["id"], "dbf": v["dbf"], "name": name, "text": text, "textHash": text_hash(text),
               "tags": sorted([n, val] for n, val in tags.items())}
        if v["refs"]:
            row["refs"] = v["refs"]
        if v["entourage"]:
            row["entourage"] = v["entourage"]
        if original:
            row["originalId"] = original["id"]
            row["originalDbf"] = original["dbf"]
        cards.append(row)
    cards.sort(key=lambda r: r["id"])
    return cards


def render(cards, sha):
    lines = ['{"schemaVersion":1,"meta":"%s",' % META, '"cards":[],',
             '"overlay":{"schemaVersion":1,"sourceBuild":"%s","exportSha256":"%s","cards":[' % (BUILD, sha)]
    body = [json.dumps(r, ensure_ascii=False, separators=(",", ":")) for r in cards]
    return "\n".join(lines) + "\n" + ",\n".join(body) + "\n]}}\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--hsdata", required=True)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    sha = sha256(args.hsdata)
    if sha != PINNED:
        sys.exit("refused: %s has SHA-256 %s, expected the pinned hsdata 254477 CardDefs.xml" % (args.hsdata, sha))
    cards = build(args.hsdata)
    outputs = {OUT: render(cards, sha)}
    stale = []
    for path, content in outputs.items():
        current = open(path, encoding="utf-8").read() if os.path.exists(path) else None
        if current != content:
            stale.append(path)
            if not args.check:
                with open(path, "w", encoding="utf-8", newline="\n") as f:
                    f.write(content)
    collectible = sum(1 for r in cards if [321, 1] in r["tags"])
    print("%s: %d rows (%d collectible), %d with an original" % (META, len(cards), collectible, sum("originalId" in r for r in cards)))
    if args.check and stale:
        sys.exit("stale: " + ", ".join(os.path.relpath(p, ROOT) for p in stale))


if __name__ == "__main__":
    main()
