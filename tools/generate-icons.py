"""Generates the full ClickTo: Images MSIX icon asset set from a single vector definition.

Run:  python tools/generate-icons.py
Writes to src/ClickToImages.Package/Images/, src/ClickToImages.App/Assets/ (the .ico) and
tools/out/ (store extras).

The mark says what the app does: a portrait source document — fanned stack, plum tile, mint
foot band, exactly as ClickTo: PDF and ClickTo: Slideshow — with the picture it turns into
sitting in front of it, a white card carrying a mint photo glyph. The card is separated from
the document by a knocked-out gap rather than an outline, so it reads as a second object on
the tile in the "tiled" treatment and as a second object on the Start tile's background in
the "glyph" treatment.

Two treatments are produced:
  - "tiled"  : the mark inside the rounded plum square, transparent outside it.
               Used where the icon renders standalone (app list, taskbar, Store).
  - "glyph"  : the mark alone on transparency. Used for Start tiles, which sit on
               the manifest's BackgroundColor (#7A2D6E).

Started from ClickToSlideshow/tools/generate-icons.py. The copies have deliberately diverged
(PDF draws a badge, Slideshow a band and a play triangle, this one a band and a card), so fix
a bug here by porting it, not by assuming a shared file.
"""

import math
import os

from PIL import Image, ImageDraw

TILE_COLOR = (0x7A, 0x2D, 0x6E, 255)
ACCENT_COLOR = (0x4F, 0xC9, 0xA0, 255)
MASTER = 2048  # supersampled master; every asset is a LANCZOS downsample of this

HERE = os.path.dirname(os.path.abspath(__file__))
IMAGES_DIR = os.path.join(HERE, "..", "src", "ClickToImages.Package", "Images")
APP_ASSETS_DIR = os.path.join(HERE, "..", "src", "ClickToImages.App", "Assets")
OUT_DIR = os.path.join(HERE, "out")

# Fraction of a tile's short edge occupied by the glyph artboard on Start tiles.
# 0.85 puts the visible mark at ~58% of the tile's short edge, which is the size
# Microsoft's tile asset guidance calls for (88px of a 150px tile at scale-100).
GLYPH_TILE_RATIO = 0.85

# The mark is defined on a 96-unit artboard. Document, then the card in front of it.
DOC = (18.0, 15.0, 57.0, 66.0)   # x0, y0, x1, y1
DOC_RADIUS = 5.0
BAND_TOP = 50.0                  # mint foot band runs from here to the document's base
GAP = (40.4, 48.4, 82.4, 81.6)   # knocked out of the mark, so the card sits clear of it
GAP_RADIUS = 6.8
CARD = (43.2, 51.2, 79.6, 78.8)
CARD_RADIUS = 4.8
GLYPH_CENTRE = (61.4, 67.2)
GLYPH_SCALE = 8.6


def photo_glyph(draw, cx, cy, s, u, fill):
    """Sun and two peaks. Chunky on purpose: this has to survive 16 px."""
    draw.ellipse(
        [(cx - 0.72 * s - 0.26 * s) * u, (cy - 0.42 * s - 0.26 * s) * u,
         (cx - 0.72 * s + 0.26 * s) * u, (cy - 0.42 * s + 0.26 * s) * u],
        fill=fill,
    )
    draw.polygon(
        [((cx - 1.05 * s) * u, (cy + 0.62 * s) * u),
         ((cx - 0.22 * s) * u, (cy - 0.40 * s) * u),
         ((cx + 0.34 * s) * u, (cy + 0.16 * s) * u),
         ((cx + 0.62 * s) * u, (cy - 0.12 * s) * u),
         ((cx + 1.05 * s) * u, (cy + 0.62 * s) * u)],
        fill=fill,
    )


def render_master(with_tile):
    """Draws the mark on a 96-unit artboard, rendered at MASTER pixels square."""
    n = MASTER
    u = n / 96.0

    base = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    if with_tile:
        ImageDraw.Draw(base).rounded_rectangle(
            [0, 0, n - 1, n - 1], radius=22 * u, fill=TILE_COLOR
        )

    doc_box = [DOC[0] * u, DOC[1] * u, DOC[2] * u, DOC[3] * u]
    doc_centre = ((DOC[0] + DOC[2]) / 2 * u, (DOC[1] + DOC[3]) / 2 * u)

    mark = Image.new("RGBA", (n, n), (0, 0, 0, 0))

    # Back to front: two ghosted sheets rotated out of the stack, then the solid front
    # sheet with its band. Angles are PIL's sign convention (positive = counter-clockwise
    # on screen), which is the opposite of SVG's rotate(); they are rotated about the
    # document's own centre, not the artboard's, because this document is not centred.
    for angle, alpha in ((15.0, 97), (7.5, 158), (0.0, 255)):
        layer = Image.new("RGBA", (n, n), (0, 0, 0, 0))
        ld = ImageDraw.Draw(layer)
        ld.rounded_rectangle(doc_box, radius=DOC_RADIUS * u, fill=(255, 255, 255, alpha))

        if angle == 0.0:
            # Mint foot band. Bottom corners follow the sheet's radius, top edge is
            # square — squaring it off is what makes the shape read as a document band
            # rather than a floating pill at 16-24 px.
            ld.rounded_rectangle(
                [DOC[0] * u, BAND_TOP * u, DOC[2] * u, DOC[3] * u],
                radius=DOC_RADIUS * u, fill=ACCENT_COLOR,
            )
            ld.rectangle(
                [DOC[0] * u, BAND_TOP * u, DOC[2] * u, (BAND_TOP + 5) * u],
                fill=ACCENT_COLOR,
            )
        else:
            layer = layer.rotate(angle, resample=Image.BICUBIC, center=doc_centre)

        mark = Image.alpha_composite(mark, layer)

    # Knock the card's gap out of the mark rather than painting it in the tile colour, so
    # the separation survives the glyph treatment, where there is no tile behind it.
    hole = Image.new("L", (n, n), 0)
    ImageDraw.Draw(hole).rounded_rectangle(
        [GAP[0] * u, GAP[1] * u, GAP[2] * u, GAP[3] * u], radius=GAP_RADIUS * u, fill=255
    )
    mark.paste((0, 0, 0, 0), (0, 0), hole)

    card = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    cd = ImageDraw.Draw(card)
    cd.rounded_rectangle(
        [CARD[0] * u, CARD[1] * u, CARD[2] * u, CARD[3] * u],
        radius=CARD_RADIUS * u, fill=(255, 255, 255, 255),
    )
    photo_glyph(cd, GLYPH_CENTRE[0], GLYPH_CENTRE[1], GLYPH_SCALE, u, ACCENT_COLOR)
    mark = Image.alpha_composite(mark, card)

    return Image.alpha_composite(base, mark)


MASTER_TILED = render_master(True)
MASTER_GLYPH = render_master(False)


def square(path, size, treatment):
    master = MASTER_TILED if treatment == "tiled" else MASTER_GLYPH

    if treatment == "tiled":
        out = master.resize((size, size), Image.LANCZOS)
    else:
        art = max(1, round(size * GLYPH_TILE_RATIO))
        out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        scaled_art = master.resize((art, art), Image.LANCZOS)
        off = (size - art) // 2
        out.alpha_composite(scaled_art, (off, off))

    write(out, path)


def wide(path, width, height):
    art = max(1, round(height * GLYPH_TILE_RATIO))
    out = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    scaled_art = MASTER_GLYPH.resize((art, art), Image.LANCZOS)
    out.alpha_composite(scaled_art, ((width - art) // 2, (height - art) // 2))
    write(out, path)


def write(img, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path, "PNG", optimize=True)
    print("wrote", os.path.relpath(path, os.path.join(HERE, "..")), img.size)


SCALES = (("100", 1.0), ("125", 1.25), ("150", 1.5), ("200", 2.0), ("400", 4.0))
TARGET_SIZES = (16, 24, 32, 48, 256)


def scaled(base, factor):
    # Windows' published per-scale asset sizes round half up (e.g. 71x71 at scale-150
    # is documented as 107, not 106) — Python's round() is banker's rounding
    # (round-half-to-even) and lands one pixel short exactly at the .5 cases this hits
    # (71*1.5=106.5, 50*1.25=62.5), which MSBuild's APPX1619 validation then rejects.
    return max(1, math.floor(base * factor + 0.5))


def main():
    img = lambda name: os.path.join(IMAGES_DIR, name)

    # Standalone icon — app list, taskbar, Store. Keeps its own plum plate.
    for base, name in ((44, "Square44x44Logo"), (50, "StoreLogo")):
        square(img(name + ".png"), base, "tiled")

        for suffix, factor in SCALES:
            square(img("%s.scale-%s.png" % (name, suffix)), scaled(base, factor), "tiled")

    # targetsize variants drive Start's "all apps" list, taskbar, and Alt+Tab.
    # altform-unplated is identical here because the mark supplies its own plate.
    for size in TARGET_SIZES:
        square(img("Square44x44Logo.targetsize-%d.png" % size), size, "tiled")
        square(
            img("Square44x44Logo.targetsize-%d_altform-unplated.png" % size), size, "tiled"
        )

    # Start tiles — drawn over the manifest BackgroundColor, so glyph only.
    for base, name in (
        (71, "Square71x71Logo"),
        (150, "Square150x150Logo"),
        (310, "Square310x310Logo"),
    ):
        square(img(name + ".png"), base, "glyph")

        for suffix, factor in SCALES:
            square(img("%s.scale-%s.png" % (name, suffix)), scaled(base, factor), "glyph")

    wide(img("Wide310x150Logo.png"), 310, 150)

    for suffix, factor in SCALES:
        wide(
            img("Wide310x150Logo.scale-%s.png" % suffix),
            scaled(310, factor), scaled(150, factor),
        )

    # Window/exe icon. AppWindow.SetIcon needs this beside the exe; the manifest logos
    # do not set the running window's icon.
    ico_sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [MASTER_TILED.resize((s, s), Image.LANCZOS) for s in ico_sizes]
    for directory in (OUT_DIR, APP_ASSETS_DIR):
        os.makedirs(directory, exist_ok=True)
        ico_path = os.path.join(directory, "ClickToImages.ico")
        frames[-1].save(ico_path, format="ICO", sizes=[(s, s) for s in ico_sizes])
        print("wrote", os.path.relpath(ico_path, os.path.join(HERE, "..")))

    # Partner Center listing logo, and the 48 px copy Bureau.Common's cross-promo panel
    # uses. The panel lists shipped apps only, so copying that one across is a release
    # step, not a build step.
    square(os.path.join(OUT_DIR, "store-logo-300x300.png"), 300, "tiled")
    square(os.path.join(OUT_DIR, "store-logo-2160x2160.png"), 2160, "tiled")
    square(os.path.join(OUT_DIR, "ClickToImages.png"), 48, "tiled")


if __name__ == "__main__":
    main()
