"""Shared markdown post-processing from convert_all.py."""
from __future__ import annotations

import re
from pathlib import Path


def enhance_markdown(raw: str, source_name: str) -> str:
    """Turn extracted slide text into cleaner, more detailed markdown."""
    lines = raw.replace("\r\n", "\n").replace("\r", "\n").split("\n")
    out: list[str] = []
    prev_blank = False

    def emit(line: str = "") -> None:
        nonlocal prev_blank
        if not line.strip():
            if not prev_blank and out:
                out.append("")
                prev_blank = True
            return
        out.append(line)
        prev_blank = False

    emit(f"# {Path(source_name).stem}")
    emit("")
    emit(f"> **Source:** `{source_name}`")
    emit("> **Converted with:** Netie Space")
    emit("")

    i = 0
    while i < len(lines):
        line = lines[i].rstrip()
        stripped = line.strip()

        if stripped == "\f" or line == "\f":
            emit("")
            emit("---")
            emit("")
            i += 1
            continue
        if "\f" in line:
            parts = line.split("\f")
            for j, part in enumerate(parts):
                if part.strip():
                    for sl in _enhance_inline_chunk(part):
                        emit(sl)
                if j < len(parts) - 1:
                    emit("")
                    emit("---")
                    emit("")
            i += 1
            continue

        formatted = _format_line(stripped, lines, i)
        if formatted is not None:
            emit(formatted)
        i += 1

    cleaned: list[str] = []
    for line in out:
        s = line.strip()
        if s in {"Wind Energy", "Hydro Energy", "### Wind Energy", "### Hydro Energy"}:
            continue
        cleaned.append(line)

    merged: list[str] = []
    i = 0
    while i < len(cleaned):
        line = cleaned[i]
        if line.startswith("## ") and not line.startswith("### "):
            parts = [line[3:].strip()]
            j = i + 1
            while j < len(cleaned) and cleaned[j].startswith("## ") and not cleaned[j].startswith("### "):
                parts.append(cleaned[j][3:].strip())
                j += 1
            merged.append("## " + " ".join(parts) if len(parts) > 1 else line)
            i = j
            continue
        merged.append(line)
        i += 1

    text = "\n".join(merged).strip() + "\n"
    return re.sub(r"\n{3,}", "\n\n", text)


def _enhance_inline_chunk(chunk: str) -> list[str]:
    result: list[str] = []
    for line in chunk.split("\n"):
        s = line.strip()
        if not s:
            continue
        formatted = _format_line(s, [s], 0)
        if formatted is not None:
            result.append(formatted)
    return result


def _format_line(stripped: str, lines: list[str], index: int) -> str | None:
    if not stripped:
        return None

    if re.match(r"^[oO•]\s+", stripped):
        return re.sub(r"^[oO•]\s+", "- ", stripped)

    if re.match(r"^\([ivx]+\)\s*$", stripped, re.I) and index + 1 < len(lines):
        nxt = lines[index + 1].strip()
        if nxt:
            return f"- **{stripped}** {nxt}"

    if (
        len(stripped) < 80
        and stripped == stripped.upper()
        and re.search(r"[A-Z]{3,}", stripped)
        and not re.fullmatch(r"[\d\s\W]+", stripped)
    ):
        return f"## {stripped.title()}"

    if re.search(r"[=∑∫]|\\frac|\(.*\).*\^", stripped) or re.fullmatch(
        r"[PWmghqρμ].*[=].*", stripped.replace(" ", "")
    ):
        return f"`{stripped}`"

    if (
        len(stripped) < 60
        and stripped[0].isupper()
        and stripped.count(" ") <= 8
        and not stripped.endswith(".")
        and index > 0
        and not lines[index - 1].strip()
    ):
        return f"### {stripped}"

    return stripped
