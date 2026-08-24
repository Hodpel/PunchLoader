"""Reflow dialogue by rendered glyph width instead of raw character count.

ASCII words and configured mixed-language phrases are indivisible blocks. The
line limit is expressed in the width of one Chinese glyph, using the advances
from the packaged fonts/visitor.tsv map.
"""

from __future__ import annotations

import argparse
import csv
import math
import re
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path


# Calibration from the 2026-08-24 runtime audit: a 30px atlas advance renders
# as 20.467 screen pixels in this dialogue TextMesh.  408px keeps the text
# inside the observed dialogue panel while matching the widest accepted lines.
DIALOGUE_PIXEL_PER_ATLAS_PIXEL = 20.467 / 30.0
MAX_LINE_PIXELS = 408.0
MAX_CJK_UNITS = MAX_LINE_PIXELS / 20.467
SPACE_CHARS = " \u2009"
CLOSING_PUNCTUATION = set(",.!?;:，。！？；：、)]}》」』…")
STRONG_END = set(".!?。！？")
SOFT_END = set(",;:，；：")
BAD_LINE_START = set("的得地了着过们而但与和或及并却也就都还又再才")
BAD_LINE_END = set("把被让给向从在对为和与或及并但而却可会能要需将我你他她它这那")
PROTECTED_PATTERNS = (
    re.compile(r"www\.[A-Za-z0-9.-]+(?:/[A-Za-z0-9_./?=&%-]*)?"),
    re.compile(r"第[ \u2009]*\d+[ \u2009]*关"),
    re.compile(r"\d+[ \u2009]*个[ \u2009]*(?:Bits?|Megacs?)", re.IGNORECASE),
    re.compile(r"(?:左|右)[ \u2009]*(?:Ctrl|Shift|Cmd)", re.IGNORECASE),
    re.compile(r"HB-02[ \u2009]*将军", re.IGNORECASE),
)
ASCII_WORD = re.compile(r"[A-Za-z0-9]+(?:[.'_-][A-Za-z0-9]+)*")


@dataclass
class Token:
    prefix: str
    text: str
    units: float


def load_advances(path: Path) -> dict[str, float]:
    values: dict[str, float] = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if not line or line.startswith("#"):
            continue
        fields = line.split("\t")
        values[chr(int(fields[0]))] = float(fields[-1]) / 30.0
    return values


def load_blocks(path: Path) -> list[str]:
    if not path.exists():
        return []
    blocks = []
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        value = line.strip()
        if value and not value.startswith("#"):
            blocks.append(value)
    return sorted(set(blocks), key=len, reverse=True)


def load_overrides(path: Path) -> dict[tuple[str, str], str]:
    if not path.exists():
        return {}
    result: dict[tuple[str, str], str] = {}
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        for row in reader:
            result[(row["group_id"], row["line_id"])] = row["translation"]
    return result


def contains_han(text: str) -> bool:
    return any("\u4e00" <= char <= "\u9fff" for char in text)


def char_units(char: str, advances: dict[str, float]) -> float:
    return advances.get(char, 1.0)


def text_units(text: str, advances: dict[str, float]) -> float:
    return sum(char_units(char, advances) for char in text)


def text_pixels(text: str, advances: dict[str, float]) -> float:
    return text_units(text, advances) * 30.0 * DIALOGUE_PIXEL_PER_ATLAS_PIXEL


def match_block(text: str, index: int, blocks: list[str]) -> str | None:
    for block in blocks:
        if text.startswith(block, index):
            return block
    for pattern in PROTECTED_PATTERNS:
        match = pattern.match(text, index)
        if match:
            return match.group(0)
    match = ASCII_WORD.match(text, index)
    return match.group(0) if match else None


def tokenize(text: str, advances: dict[str, float], blocks: list[str]) -> list[Token]:
    tokens: list[Token] = []
    pending_space = ""
    index = 0
    while index < len(text):
        char = text[index]
        if char in SPACE_CHARS:
            pending_space += char
            index += 1
            continue
        block = match_block(text, index, blocks)
        value = block if block is not None else char
        index += len(value)
        if value in CLOSING_PUNCTUATION and tokens and not pending_space:
            tokens[-1].text += value
            tokens[-1].units += text_units(value, advances)
            continue
        tokens.append(Token(pending_space, value, text_units(value, advances)))
        pending_space = ""
    if pending_space and tokens:
        tokens[-1].text += pending_space
        tokens[-1].units += text_units(pending_space, advances)
    return tokens


def wrap_tokens(tokens: list[Token], advances: dict[str, float]) -> list[str]:
    if not tokens:
        return []

    @lru_cache(maxsize=None)
    def line_text(start: int, end: int) -> str:
        parts = []
        for position in range(start, end):
            token = tokens[position]
            parts.append(("" if position == start else token.prefix) + token.text)
        return "".join(parts).rstrip(SPACE_CHARS)

    @lru_cache(maxsize=None)
    def line_units(start: int, end: int) -> float:
        return text_units(line_text(start, end), advances)

    @lru_cache(maxsize=None)
    def line_pixels(start: int, end: int) -> float:
        return text_pixels(line_text(start, end), advances)

    # First determine the minimum number of lines needed.  The second pass then
    # balances those lines instead of greedily producing an almost-empty tail.
    total = text_pixels("".join(token.prefix + token.text for token in tokens), advances)
    minimum_lines = max(1, int(math.ceil(total / MAX_LINE_PIXELS)))

    def solve(line_count: int) -> tuple[float, tuple[int, ...]] | None:
        target = total / line_count

        @lru_cache(maxsize=None)
        def visit(start: int, remaining: int) -> tuple[float, tuple[int, ...]] | None:
            if remaining == 0:
                return (0.0, ()) if start == len(tokens) else None
            if len(tokens) - start < remaining:
                return None
            best = None
            last_end = len(tokens) - remaining + 1
            for end in range(start + 1, last_end + 1):
                width = line_pixels(start, end)
                oversized_single_block = end == start + 1 and width > MAX_LINE_PIXELS
                if width > MAX_LINE_PIXELS + 0.01 and not oversized_single_block:
                    break
                tail = visit(end, remaining - 1)
                if tail is None:
                    continue
                rendered = line_text(start, end)
                ending = rendered[-1:]
                next_start = tokens[end].text[:1] if end < len(tokens) else ""
                # Prefer clause/sentence boundaries over mathematically even
                # but linguistically broken lines.  Width balance is only the
                # tie-breaker after boundary quality.
                if ending in STRONG_END and width >= MAX_LINE_PIXELS * 0.30:
                    boundary_penalty = 0.0
                elif ending in SOFT_END and width >= MAX_LINE_PIXELS * 0.30:
                    boundary_penalty = 0.25
                elif end < len(tokens) and tokens[end].prefix:
                    boundary_penalty = 2.0
                else:
                    boundary_penalty = 6.0
                if ending in BAD_LINE_END:
                    boundary_penalty += 12.0
                if next_start in BAD_LINE_START:
                    boundary_penalty += 12.0
                raggedness = ((width - target) / MAX_LINE_PIXELS) ** 2
                score = boundary_penalty + raggedness + tail[0]
                candidate = (score, (end,) + tail[1])
                if best is None or candidate[0] < best[0]:
                    best = candidate
            return best

        return visit(0, line_count)

    solution = None
    for line_count in range(minimum_lines, len(tokens) + 1):
        solution = solve(line_count)
        if solution is not None:
            break
    if solution is None:
        raise RuntimeError("Unable to wrap token sequence")

    lines = []
    start = 0
    for end in solution[1]:
        lines.append(line_text(start, end))
        start = end
    return lines


def reflow(text: str, advances: dict[str, float], blocks: list[str]) -> str:
    # Remove the old visual break and its padding.  When the break previously
    # separated two word characters, retain a thin mixed-text space so tokens
    # such as "64 / 个" and "去 / www..." do not get concatenated.
    def remove_old_break(match: re.Match[str]) -> str:
        left = text[match.start() - 1] if match.start() else ""
        right = text[match.end()] if match.end() < len(text) else ""
        left_ascii = left.isascii() and left.isalnum()
        right_ascii = right.isascii() and right.isalnum()
        keep_separator = (left_ascii and right.isalnum()) or (right_ascii and left.isalnum())
        return "\u2009" if keep_separator else ""

    flattened = re.sub(r"[ \u2009]*\\n[ \u2009]*", remove_old_break, text)
    return "\\n".join(wrap_tokens(tokenize(flattened, advances, blocks), advances))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--check", action="store_true", help="calculate only; do not rewrite TSV")
    parser.add_argument("--report", type=Path, help="write the before/after width report")
    args = parser.parse_args()
    mod_path = args.root / "mods" / "ChineseLocalization"
    translation_path = mod_path / "data" / "dialogue.tsv"
    glyph_path = mod_path / "fonts" / "visitor.tsv"
    tooling_path = args.root / "tools" / "localization"
    block_path = tooling_path / "dialogue_wrap_blocks.txt"
    override_path = tooling_path / "dialogue_wrap_overrides.tsv"
    metrics = load_advances(glyph_path)
    blocks = load_blocks(block_path)
    overrides = load_overrides(override_path)
    source_bytes = translation_path.read_bytes()
    has_bom = source_bytes.startswith(b"\xef\xbb\xbf")
    newline = "\r\n" if b"\r\n" in source_bytes else "\n"
    with translation_path.open("r", encoding="utf-8-sig", newline="") as handle:
        header = handle.readline().rstrip("\r\n")
        rows = []
        for raw in handle:
            fields = raw.rstrip("\r\n").split("\t", 3)
            if len(fields) != 4:
                raise ValueError("Invalid dialogue row: " + raw)
            original = fields[3]
            key = (fields[0], fields[1])
            if key in overrides:
                fields[3] = overrides[key]
            elif not contains_han(fields[2]) and not contains_han(original):
                # Pure ASCII dialogue keeps the game's original content and
                # authored line breaks; only Chinese/mixed text is reflowed.
                fields[3] = fields[2]
            else:
                fields[3] = reflow(original, metrics, blocks)
            rows.append((fields, original))
    changed = sum(fields[3] != original for fields, original in rows)
    violations = []
    for fields, _ in rows:
        for line in fields[3].split("\\n"):
            units = text_units(line, metrics)
            if units > MAX_CJK_UNITS + 0.001:
                violations.append((fields[0], fields[1], units, line))
    print(f"rows={len(rows)} changed={changed} max_pixels={MAX_LINE_PIXELS:.1f} blocks={len(blocks)} overrides={len(overrides)} violations={len(violations)}")
    for item in violations[:20]:
        print("oversized-block", item[0], item[1], f"{item[2]:.2f}", item[3])
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        with args.report.open("w", encoding="utf-8-sig", newline="") as handle:
            writer = csv.writer(handle, delimiter="\t")
            writer.writerow(("group", "line", "changed", "before", "after", "after_widths"))
            for fields, original in rows:
                widths = ",".join(f"{text_pixels(line, metrics):.1f}px" for line in fields[3].split("\\n"))
                writer.writerow((fields[0], fields[1], int(fields[3] != original), original, fields[3], widths))
    if args.check:
        return
    output = [header] + ["\t".join(fields) for fields, _ in rows]
    encoded = (newline.join(output) + newline).encode("utf-8")
    if has_bom:
        encoded = b"\xef\xbb\xbf" + encoded
    translation_path.write_bytes(encoded)


if __name__ == "__main__":
    main()
