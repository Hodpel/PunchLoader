"""Build the ChineseLocalization bitmap font atlas deterministically.

Usage (from repository root):
  C:\\Users\\HODPEL\\AppData\\Local\\Programs\\Python\\Python311\\python.exe tools\\build_font_atlas.py
"""

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
MOD = ROOT / "mods" / "ChineseLocalization"
FONT = Path(r"C:\Windows\Fonts\NotoSansSC-VF.ttf")
TRANSLATIONS = MOD / "translations.tsv"
PNG = MOD / "font_atlas.png"
MAP = MOD / "glyphs.tsv"

FONT_SIZE = 64
PADDING = 4
MAX_WIDTH = 2048


def read_targets():
    targets = []
    for line in TRANSLATIONS.read_text(encoding="utf-8-sig").splitlines():
        if not line or line.startswith("#"):
            continue
        source, target = line.split("\t", 1)
        if not source or not target:
            raise ValueError("invalid translation row: " + line)
        targets.append(target)
    return targets


def next_power_of_two(value):
    result = 1
    while result < value:
        result *= 2
    return result


def make_font():
    if not FONT.exists():
        raise FileNotFoundError("Noto Sans SC was not found: " + str(FONT))
    font = ImageFont.truetype(str(FONT), FONT_SIZE)
    font.set_variation_by_name("Regular")
    return font


def collect_characters(targets):
    chars = {chr(code) for code in range(32, 127)}
    chars.update("。，、；：？！…—～【】（）《》·「」『』“”‘’")
    for target in targets:
        chars.update(target)
    return sorted(chars, key=ord)


def metrics(font, char):
    box = font.getbbox(char)
    advance = int(round(font.getlength(char)))
    if box is None or box[2] <= box[0] or box[3] <= box[1]:
        # Space has no pixels.  Give it a tiny transparent rectangle and real advance.
        return 1, 1, 0, 0, max(advance, FONT_SIZE // 3)
    return box[2] - box[0], box[3] - box[1], box[0], box[1], max(advance, box[2] - box[0])


def build():
    font = make_font()
    characters = collect_characters(read_targets())
    glyphs = []
    max_glyph_width = 0
    max_glyph_height = 0
    for char in characters:
        width, height, left, top, advance = metrics(font, char)
        glyphs.append((char, width, height, left, top, advance))
        max_glyph_width = max(max_glyph_width, width)
        max_glyph_height = max(max_glyph_height, height)

    cell_width = max_glyph_width + PADDING * 2
    cell_height = max_glyph_height + PADDING * 2
    best_layout = None
    for candidate_columns in range(1, MAX_WIDTH // cell_width + 1):
        candidate_rows = (len(glyphs) + candidate_columns - 1) // candidate_columns
        candidate_width = next_power_of_two(candidate_columns * cell_width)
        candidate_height = next_power_of_two(candidate_rows * cell_height)
        if candidate_width > MAX_WIDTH or candidate_height > MAX_WIDTH:
            continue
        score = (candidate_width * candidate_height,
                 abs(candidate_width - candidate_height), candidate_columns)
        if best_layout is None or score < best_layout[0]:
            best_layout = (score, candidate_columns, candidate_rows,
                           candidate_width, candidate_height)
    if best_layout is None:
        raise ValueError("glyph set exceeds the 2048x2048 Unity texture limit")
    _, columns, rows, atlas_width, atlas_height = best_layout

    atlas = Image.new("RGBA", (atlas_width, atlas_height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(atlas)
    rows_out = ["# atlas\t%d\t%d\t%d" % (atlas_width, atlas_height, FONT_SIZE)]

    for index, (char, width, height, left, top, advance) in enumerate(glyphs):
        column = index % columns
        row = index // columns
        x = column * cell_width + PADDING
        y = row * cell_height + PADDING
        if char != " ":
            draw.text((x - left, y - top), char, font=font, fill=(255, 255, 255, 255))
        # code, atlas rect, Unity CharacterInfo.vert, advance
        rows_out.append("%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d" % (
            ord(char), x, y, width, height, left, -top, width, -height, advance))

    atlas.save(PNG, "PNG", optimize=True)
    MAP.write_text("\n".join(rows_out) + "\n", encoding="utf-8")
    print("font=%s" % font.getname()[0])
    print("glyphs=%d atlas=%dx%d cell=%dx%d" % (
        len(glyphs), atlas_width, atlas_height, cell_width, cell_height))
    print("wrote=%s" % PNG)
    print("wrote=%s" % MAP)


if __name__ == "__main__":
    build()
