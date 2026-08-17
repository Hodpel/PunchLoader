"""Build the ChineseLocalization composite bitmap font atlas.

ASCII glyphs are decoded from the game's original ACKNOWTT menu font; Chinese
glyphs are rendered at native size from BoutiqueBitmap 9x9 Bold.  The resulting
single Unity bitmap font therefore keeps English visually unchanged while using
crisp pixel Chinese at the same 19-pixel visible cap height.

Usage from repository root:
  py -3.11 tools\build_font_atlas.py ^
    --ack-font ..\文件整理\ExportedProject\Assets\Font\ACKNOWTT_white.asset ^
    --ack-texture ..\文件整理\ExportedProject\Assets\Texture2D\Font Texture_0.texture2D
"""

import argparse
import re
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
MOD = ROOT / "mods" / "ChineseLocalization"
FONT = ROOT / "tools" / "fonts" / "BoutiqueBitmap9x9_Bold_1.93.ttf"
TRANSLATIONS = MOD / "translations.tsv"
PART_TRANSLATIONS = MOD / "part_translations.tsv"
PNG = MOD / "font_atlas.png"
MAP = MOD / "glyphs.tsv"
SMALL_PNG = MOD / "font_atlas_small.png"
SMALL_MAP = MOD / "glyphs_small.tsv"
PART_PNG = MOD / "part_font_atlas.png"
PART_MAP = MOD / "part_glyphs.tsv"
MIXED_TEXT_SPACE = "\u2009"

# Unity uses the Font fontSize as the default GUI line-height basis.  Preserve
# ACKNOWTT's original 50px font size / 44.5px line spacing so GUILayout retains
# the game's menu rhythm.  BoutiqueBitmap itself is rasterized independently at
# 25px for a 23px large-menu glyph and 19px for an 18px small-menu glyph.
# Neither variant is horizontally scaled or filtered.  Equipment names retain
# their separately accepted 21px face.
FONT_SIZE = 50
BOUTIQUE_RASTER_SIZE = 25
BOUTIQUE_TARGET_HEIGHT = 23
ACK_CAP_Y = -17.55
SMALL_FONT_SIZE = 35
SMALL_BOUTIQUE_RASTER_SIZE = 19
SMALL_BOUTIQUE_TARGET_HEIGHT = 18
SMALL_ACK_CAP_Y = -11.585
PART_BOUTIQUE_RASTER_SIZE = 25
PART_BOUTIQUE_TARGET_HEIGHT = 23
PADDING = 2
MAX_SIZE = 2048


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--ack-font", type=Path, required=True,
                        help="exported ACKNOWTT_white.asset")
    parser.add_argument("--ack-texture", type=Path, required=True,
                        help="exported Font Texture_0.texture2D")
    parser.add_argument("--ack-small-font", type=Path, required=True,
                        help="exported ACKNOWTT_white_small.asset")
    parser.add_argument("--ack-small-texture", type=Path, required=True,
                        help="exported Font Texture_2.texture2D")
    return parser.parse_args()


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


def read_part_targets():
    targets = []
    for line in PART_TRANSLATIONS.read_text(encoding="utf-8-sig").splitlines():
        if not line:
            continue
        fields = line.split("\t", 2)
        if len(fields) != 3 or fields[0] == "kind":
            continue
        targets.append(fields[2].replace("\\n", "\n"))
    return targets


def next_power_of_two(value):
    result = 1
    while result < value:
        result *= 2
    return result


def parse_ack_glyphs(font_path, texture_path):
    """Return original ASCII glyph alpha images and their Unity metrics."""
    texture_text = texture_path.read_text(encoding="utf-8")
    payload = re.search(r"_typelessdata: ([0-9a-f]+)", texture_text).group(1)
    width = int(re.search(r"m_Width: (\d+)", texture_text).group(1))
    height = int(re.search(r"m_Height: (\d+)", texture_text).group(1))
    atlas = Image.frombytes("L", (width, height), bytes.fromhex(payload))
    pattern = re.compile(
        r"index: (?P<index>\d+)\s+uv:\s+serializedVersion: 2\s+"
        r"x: (?P<uvx>-?[\d.]+)\s+y: (?P<uvy>-?[\d.]+)\s+"
        r"width: (?P<uvw>-?[\d.]+)\s+height: (?P<uvh>-?[\d.]+)\s+"
        r"vert:\s+serializedVersion: 2\s+x: (?P<vx>-?[\d.]+)\s+"
        r"y: (?P<vy>-?[\d.]+)\s+width: (?P<vw>-?[\d.]+)\s+"
        r"height: (?P<vh>-?[\d.]+)\s+width: (?P<advance>-?[\d.]+)\s+"
        r"flipped: (?P<flipped>\d+)", re.MULTILINE)
    glyphs = {}
    for match in pattern.finditer(font_path.read_text(encoding="utf-8")):
        code = int(match.group("index"))
        if code < 32 or code > 126:
            continue
        values = {name: float(item) for name, item in match.groupdict().items()
                  if name not in {"index", "flipped"}}
        width = round(abs(values["uvw"]) * atlas.width)
        height = round(abs(values["uvh"]) * atlas.height)
        left = round(values["uvx"] * atlas.width)
        # Exported Alpha8 data is bottom-row first; negative uv.height gives
        # the glyph's lower edge.  Restore its Unity packed orientation here.
        top = round((values["uvy"] + values["uvh"]) * atlas.height)
        image = atlas.crop((left, top, left + width, top + height))
        if int(match.group("flipped")):
            image = image.transpose(Image.Transpose.ROTATE_270)
            image = image.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
        glyphs[chr(code)] = (image, values["vx"], values["vy"],
                             values["vw"], values["vh"], values["advance"])
    missing = [chr(code) for code in range(32, 127) if chr(code) not in glyphs]
    if missing:
        raise ValueError("ACKNOWTT atlas lacks ASCII: " + "".join(missing))
    return glyphs


def collect_characters(targets):
    chars = set("。，、；：？！…—～【】（）《》·「」『』“”‘’")
    for target in targets:
        chars.update(target)
    # ASCII is supplied exclusively by the original ACKNOWTT atlas.
    return sorted((char for char in chars if ord(char) > 126), key=ord)


def boutique_glyph(font, char, cap_y, raster_size, target_height):
    box = font.getbbox(char)
    advance = int(round(font.getlength(char)))
    if box is None or box[2] <= box[0] or box[3] <= box[1]:
        image = Image.new("L", (1, 1), 0)
        return image, 0, cap_y, 1, -1, max(advance, raster_size // 2)
    width = box[2] - box[0]
    height = box[3] - box[1]
    image = Image.new("L", (width, height), 0)
    ImageDraw.Draw(image).text((-box[0], -box[1]), char, font=font, fill=255)
    # Bitmap-only alpha: this must remain binary in Unity when Point filtered.
    image = image.point(lambda value: 255 if value >= 128 else 0)
    if image.height != target_height:
        image = image.resize((image.width, target_height), Image.Resampling.NEAREST)
    # `cap_y` is chosen so the CJK character box shares ACKNOWTT's capital
    # vertical centre, rather than its top edge.  This keeps a 23px Chinese
    # glyph centred against a 19px ASCII cap, and likewise for the small face.
    return image, box[0], cap_y, image.width, -image.height, max(advance, image.width)


def choose_layout(glyphs):
    max_width = max(image.width for _, image, *_ in glyphs)
    max_height = max(image.height for _, image, *_ in glyphs)
    cell_width = max_width + PADDING * 2
    cell_height = max_height + PADDING * 2
    best = None
    for columns in range(1, MAX_SIZE // cell_width + 1):
        rows = (len(glyphs) + columns - 1) // columns
        width = next_power_of_two(columns * cell_width)
        height = next_power_of_two(rows * cell_height)
        if width > MAX_SIZE or height > MAX_SIZE:
            continue
        score = (width * height, abs(width - height), columns)
        if best is None or score < best[0]:
            best = (score, columns, width, height, cell_width, cell_height)
    if best is None:
        raise ValueError("glyph set exceeds %dx%d Unity texture limit" % (MAX_SIZE, MAX_SIZE))
    return best


def build_variant(ack, targets, raster_size, target_height, font_size, cap_y, png_path, map_path,
                  center_cjk=True):
    boutique = ImageFont.truetype(str(FONT), raster_size)
    ascii_cap_height = abs(ack["A"][4])
    cjk_cap_y = cap_y + (target_height - ascii_cap_height) * 0.5 if center_cjk else cap_y
    glyphs = []
    for char in sorted(ack):
        glyphs.append((char,) + ack[char])
    for char in collect_characters(targets):
        if char == MIXED_TEXT_SPACE:
            # The game's normal ASCII space is intentionally broad.  This
            # transparent glyph is used only between CJK and ASCII, at half
            # of the original ACKNOWTT space advance.
            _, vx, vy, _, _, advance = ack[" "]
            glyphs.append((char, Image.new("L", (1, 1), 0), vx, vy, 0, 0, advance * 0.5))
            continue
        glyphs.append((char,) + boutique_glyph(boutique, char, cjk_cap_y, raster_size, target_height))

    _, columns, atlas_width, atlas_height, cell_width, cell_height = choose_layout(glyphs)
    atlas = Image.new("RGBA", (atlas_width, atlas_height), (0, 0, 0, 0))
    rows = ["# atlas\t%d\t%d\t%d" % (atlas_width, atlas_height, font_size)]
    for index, (char, image, vx, vy, vw, vh, advance) in enumerate(glyphs):
        x = (index % columns) * cell_width + PADDING
        y = (index // columns) * cell_height + PADDING
        color = Image.new("RGBA", image.size, (255, 255, 255, 255))
        color.putalpha(image)
        atlas.alpha_composite(color, (x, y))
        rows.append("%d\t%d\t%d\t%d\t%d\t%.4f\t%.4f\t%.4f\t%.4f\t%.4f" % (
            ord(char), x, y, image.width, image.height, vx, vy, vw, vh, advance))

    atlas.save(png_path, "PNG", optimize=True)
    map_path.write_text("\n".join(rows) + "\n", encoding="utf-8")
    cjk_count = len(glyphs) - len(ack)
    print("font=BoutiqueBitmap 9x9 Bold 1.93 (Add Spacing), fontSize=%d" % font_size)
    print("glyphs=%d (ACKNOWTT ASCII=%d, Boutique CJK=%d) atlas=%dx%d" %
          (len(glyphs), len(ack), cjk_count, atlas_width, atlas_height))
    print("wrote=" + str(png_path))
    print("wrote=" + str(map_path))


def build(args):
    if not FONT.exists():
        raise FileNotFoundError("BoutiqueBitmap source is missing: " + str(FONT))
    for path in (args.ack_font, args.ack_texture, args.ack_small_font, args.ack_small_texture):
        if not path.exists():
            raise FileNotFoundError("ACKNOWTT source asset or texture is missing: " + str(path))

    menu_targets = read_targets()
    part_targets = read_part_targets()
    ack = parse_ack_glyphs(args.ack_font, args.ack_texture)
    build_variant(ack, menu_targets,
                  BOUTIQUE_RASTER_SIZE, BOUTIQUE_TARGET_HEIGHT, FONT_SIZE, ACK_CAP_Y, PNG, MAP)
    build_variant(parse_ack_glyphs(args.ack_small_font, args.ack_small_texture), menu_targets,
                  SMALL_BOUTIQUE_RASTER_SIZE, SMALL_BOUTIQUE_TARGET_HEIGHT,
                  SMALL_FONT_SIZE, SMALL_ACK_CAP_Y,
                  SMALL_PNG, SMALL_MAP)
    # The inventory and collection use the same 50px ACKNOWTT face as menus,
    # but need the complete 150-part vocabulary rather than menu labels.  Their
    # Chinese glyphs use the same 23px visual height as the large menu face,
    # while ACKNOWTT ASCII remains byte-for-byte from the original atlas.
    build_variant(ack, part_targets,
                  PART_BOUTIQUE_RASTER_SIZE, PART_BOUTIQUE_TARGET_HEIGHT, FONT_SIZE, ACK_CAP_Y,
                  PART_PNG, PART_MAP, center_cjk=True)


if __name__ == "__main__":
    build(parse_args())
