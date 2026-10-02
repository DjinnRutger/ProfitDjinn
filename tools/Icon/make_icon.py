"""Build src/ProfitDjinn.App/Assets/app.ico from Assets/genie.png.

The genie sits on a rounded navy tile (the sidebar colour). From 40px up the icon
shows the whole genie; at 16-32px it switches to a close-up of the face, because
the full figure turns to mush that small.

Run with any Python that has Pillow:
    python tools/Icon/make_icon.py [preview_dir]
A preview sheet of every size is written to preview_dir when one is given.
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "src" / "ProfitDjinn.App" / "Assets"
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SMALL = 32                              # this size and below use the face crop
NAVY_TOP = (27, 52, 112)                # lighter navy at the top of the tile
NAVY = (11, 26, 63)                     # #0B1A3F, the sidebar
FACE_BOX = (42, 12, 176, 150)           # head and topknot in genie.png (200x267)
SS = 4                                  # supersampling for the tile edges


def tile(size: int) -> Image.Image:
    """Rounded navy square with a soft top-to-bottom gradient and a faint glow."""
    s = size * SS
    grad = Image.new("RGBA", (1, s))
    for y in range(s):
        t = y / (s - 1)
        grad.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(NAVY_TOP, NAVY)) + (255,))
    grad = grad.resize((s, s))

    glow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse((s * 0.12, s * 0.10, s * 0.88, s * 0.86), fill=(64, 140, 255, 70))
    grad.alpha_composite(glow.filter(ImageFilter.GaussianBlur(s * 0.12)))

    mask = Image.new("L", (s, s), 0)
    inset = 0 if size <= 24 else s * 0.03
    ImageDraw.Draw(mask).rounded_rectangle((inset, inset, s - 1 - inset, s - 1 - inset),
                                           radius=s * 0.22, fill=255)
    out = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    out.paste(grad, (0, 0), mask)
    return out.resize((size, size), Image.LANCZOS)


def place(base: Image.Image, art: Image.Image, fill: float, sharpen: bool) -> Image.Image:
    """Scale art to `fill` of the tile (by its longer side), centre it, composite."""
    size = base.width
    scale = fill * size / max(art.size)
    w, h = max(1, round(art.width * scale)), max(1, round(art.height * scale))
    art = art.resize((w, h), Image.LANCZOS)
    if sharpen:
        art = art.filter(ImageFilter.UnsharpMask(radius=0.6, percent=80, threshold=1))
    out = base.copy()
    out.alpha_composite(art, ((size - w) // 2, (size - h) // 2 + (0 if size <= SMALL else round(size * 0.01))))
    out.putalpha(base.getchannel("A"))  # nothing outside the rounded tile
    return out


def drop_money(art: Image.Image) -> Image.Image:
    """Keep only the genie: the opaque region joined to the centre of the face crop.
    The floating bills are separate shapes, so they are cleared."""
    w, h = art.size
    alpha = art.getchannel("A").load()
    keep = set()
    stack = [(w // 2, h // 2)]
    while stack:
        x, y = stack.pop()
        if (x, y) in keep or not (0 <= x < w and 0 <= y < h) or alpha[x, y] < 24:
            continue
        keep.add((x, y))
        stack += [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
    px = art.load()
    for y in range(h):
        for x in range(w):
            if (x, y) not in keep:
                px[x, y] = (0, 0, 0, 0)
    return art


def build(genie: Image.Image) -> list[Image.Image]:
    full = genie.crop(genie.split()[3].getbbox())
    face = drop_money(genie.crop(FACE_BOX))
    images = []
    for size in SIZES:
        if size <= SMALL:
            images.append(place(tile(size), face, 0.92, sharpen=True))
        else:
            images.append(place(tile(size), full, 0.86, sharpen=size <= 64))
    return images


def preview(images: list[Image.Image], folder: Path) -> None:
    """Each size at 1:1 on light and dark strips, plus a 4x zoom of the small ones."""
    folder.mkdir(parents=True, exist_ok=True)
    width = sum(i.width for i in images) + 20 * (len(images) + 1)
    sheet = Image.new("RGBA", (width, 2 * 296), (255, 255, 255, 255))
    ImageDraw.Draw(sheet).rectangle((0, 296, width, 592), fill=(32, 32, 32, 255))
    for row in (0, 296):
        x = 20
        for img in images:
            sheet.alpha_composite(img, (x, row + 20))
            x += img.width + 20
    sheet.save(folder / "icon-sizes.png")

    zoom = [i.resize((i.width * 4, i.height * 4), Image.NEAREST) for i in images if i.width <= 48]
    strip = Image.new("RGBA", (sum(z.width for z in zoom) + 20 * (len(zoom) + 1), 232), (240, 240, 240, 255))
    x = 20
    for z in zoom:
        strip.alpha_composite(z, (x, 20))
        x += z.width + 20
    strip.save(folder / "icon-small-4x.png")


def main() -> None:
    genie = Image.open(ASSETS / "genie.png").convert("RGBA")
    images = build(genie)
    target = ASSETS / "app.ico"
    images[-1].save(target, format="ICO", sizes=[(s, s) for s in SIZES], append_images=images[:-1])
    print(f"wrote {target} ({target.stat().st_size} bytes, sizes {SIZES})")
    if len(sys.argv) > 1:
        preview(images, Path(sys.argv[1]))
        print(f"preview in {sys.argv[1]}")


if __name__ == "__main__":
    main()
