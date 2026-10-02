"""Bootstrap Icons SVGs -> src/ProfitDjinn.App/Assets/bootstrap-icons.txt.

Each output line is   name<TAB>rule:path<TAB>rule:path ...
where rule is N (nonzero, the SVG default) or E (evenodd), and path is WPF path markup
with every number and command separated, because SVG's compact forms (".5.5", arc flags
written "01") are not all understood by WPF's parser. The Icon control draws each path
with its own fill rule, since some icons mix them.

    python tools\\Icons\\make_icons.py <bootstrap-icons package dir>

The package is the npm tarball for bootstrap-icons 1.11.3 (MIT), unpacked.
"""
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "src" / "ProfitDjinn.App" / "Assets" / "bootstrap-icons.txt"
NS = "{http://www.w3.org/2000/svg}"

NUM = re.compile(r"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")
ARGS = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7, "Z": 0}


def tokens(d):
    """Commands and numbers, with arc flags split even when written together."""
    out, i, cmd, argi = [], 0, None, 0
    while i < len(d):
        ch = d[i]
        if ch.isspace() or ch == ",":
            i += 1
            continue
        if ch.isalpha():
            cmd, argi = ch, 0
            out.append(ch)
            i += 1
            continue
        # Arc flags (4th and 5th argument of A) are single 0/1 digits, no separator needed.
        if cmd and cmd.upper() == "A" and argi % 7 in (3, 4):
            out.append(ch)
            i += 1
            argi += 1
            continue
        m = NUM.match(d, i)
        if not m:
            raise ValueError(f"cannot parse path data at {d[i:i+20]!r}")
        out.append(m.group(0))
        i = m.end()
        argi += 1
    return out


def clean(d):
    """Re-emit SVG path data as WPF path markup, with explicit commands and separators."""
    out, toks, i, cmd = [], tokens(d), 0, None
    while i < len(toks):
        t = toks[i]
        if t.isalpha():
            cmd = t
            out.append(cmd)
            i += 1
            if cmd.upper() == "Z":
                continue
        n = ARGS[cmd.upper()]
        args = toks[i:i + n]
        if len(args) < n or any(a.isalpha() for a in args):
            raise ValueError(f"short argument list for {cmd} in {d[:40]!r}")
        out.append(" ".join(args))
        i += n
        # After a moveto, extra coordinate pairs are implicit linetos.
        if cmd == "M":
            cmd = "L"
        elif cmd == "m":
            cmd = "l"
        if i < len(toks) and not toks[i].isalpha():
            out.append(cmd)
    return " ".join(out)


def circle(e):
    cx, cy, r = (float(e.get(k, 0)) for k in ("cx", "cy", "r"))
    return f"M {cx - r} {cy} A {r} {r} 0 1 0 {cx + r} {cy} A {r} {r} 0 1 0 {cx - r} {cy} Z"


def rect(e):
    x, y, w, h = (float(e.get(k, 0)) for k in ("x", "y", "width", "height"))
    rx = float(e.get("rx", e.get("ry", 0)))
    if rx == 0:
        return f"M {x} {y} H {x + w} V {y + h} H {x} Z"
    return (f"M {x + rx} {y} H {x + w - rx} A {rx} {rx} 0 0 1 {x + w} {y + rx} V {y + h - rx} "
            f"A {rx} {rx} 0 0 1 {x + w - rx} {y + h} H {x + rx} A {rx} {rx} 0 0 1 {x} {y + h - rx} "
            f"V {y + rx} A {rx} {rx} 0 0 1 {x + rx} {y} Z")


def main(package):
    icons = sorted((Path(package) / "icons").glob("*.svg"))
    lines, skipped = [], []
    for svg in icons:
        root = ET.parse(svg).getroot()
        parts = []
        for e in root.iter():
            tag = e.tag.replace(NS, "")
            if e.get("transform"):
                parts = None
                break
            rule = "E" if e.get("fill-rule") == "evenodd" else "N"
            if tag == "path":
                parts.append(f"{rule}:{clean(e.get('d'))}")
            elif tag == "circle":
                parts.append(f"{rule}:{circle(e)}")
            elif tag == "rect":
                parts.append(f"{rule}:{rect(e)}")
        if not parts:
            skipped.append(svg.stem)
            continue
        lines.append(svg.stem + "\t" + "\t".join(parts))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("# Bootstrap Icons 1.11.3 (MIT, https://icons.getbootstrap.com), converted by tools/Icons/make_icons.py\n"
                   + "\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(f"{len(lines)} icons -> {OUT}" + (f"; skipped (transform): {', '.join(skipped)}" if skipped else ""))


if __name__ == "__main__":
    main(sys.argv[1])
