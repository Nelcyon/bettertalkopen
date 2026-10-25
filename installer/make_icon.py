"""Draws BetterTalk.ico: the yellow speech bubble of the tray icon, at every size Windows asks for.

The shape is the tray icon's (a 32-unit design: bubble, tail and three black dots). Each size is drawn
8 times larger and scaled down, and on the small sizes the dots are put on whole pixels so they stay sharp.
Run:  python installer/make_icon.py      (needs Pillow; writes src/Aac.App/Assets/BetterTalk.ico)
"""
import io
import struct
from pathlib import Path

from PIL import Image, ImageDraw

YELLOW = (255, 212, 0, 255)
BLACK = (0, 0, 0, 255)
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SS = 8  # supersampling


def draw(size: int) -> Image.Image:
    k = size / 32 * SS
    img = Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))
    g = ImageDraw.Draw(img)
    g.ellipse([1 * k, 2 * k, 31 * k, 24 * k], fill=YELLOW)
    g.polygon([(7 * k, 19 * k), (4 * k, 30 * k), (15 * k, 22 * k)], fill=YELLOW)
    # Dots: 4 units wide, 2 units apart, centred in the bubble; snapped to whole pixels of the final size.
    d = max(2, round(4 * size / 32))
    gap = max(1, round(2 * size / 32))
    left = round(16 * size / 32 - (3 * d + 2 * gap) / 2)
    top = round(13 * size / 32 - d / 2)
    for i in range(3):
        x = left + i * (d + gap)
        g.ellipse([x * SS, top * SS, (x + d) * SS - 1, (top + d) * SS - 1], fill=BLACK)
    return img.resize((size, size), Image.LANCZOS)


def bmp_entry(img: Image.Image) -> bytes:
    """A classic 32-bit DIB entry (what older Windows parts read best for the small sizes)."""
    w, h = img.size
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    rows = []
    px = img.load()
    for y in range(h - 1, -1, -1):
        rows.append(b"".join(struct.pack("BBBB", *(px[x, y][2::-1] + (px[x, y][3],))) for x in range(w)))
    mask_row = ((w + 31) // 32) * 4
    mask = bytes(mask_row * h)  # all opaque: the alpha channel does the work
    return header + b"".join(rows) + mask


def png_entry(img: Image.Image) -> bytes:
    buf = io.BytesIO()
    img.save(buf, "PNG", optimize=True)
    return buf.getvalue()


def main() -> None:
    images = [draw(s) for s in SIZES]
    datas = [png_entry(i) if i.width >= 128 else bmp_entry(i) for i in images]
    out = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    for img, data in zip(images, datas):
        w = img.width % 256
        out += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    out += b"".join(datas)
    target = Path(__file__).resolve().parent.parent / "src" / "Aac.App" / "Assets" / "BetterTalk.ico"
    target.write_bytes(out)
    print(f"{target}: {len(out)} bytes, sizes {SIZES}")


if __name__ == "__main__":
    main()
