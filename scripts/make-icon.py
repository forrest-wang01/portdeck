"""Generate the PortDeck Windows icon and tray PNG.

Run with: python scripts/make-icon.py
Requires Pillow.
"""

from __future__ import annotations

import os

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Assets")
SIZE = 1024


def lerp(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def rounded_mask(size: int, radius: int) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return mask


def main() -> None:
    os.makedirs(ASSETS, exist_ok=True)

    mask = rounded_mask(SIZE, 224)

    # Vertical gradient background.
    bg = Image.new("RGBA", (SIZE, SIZE))
    top = (10, 20, 32)
    bottom = (6, 13, 22)
    draw = ImageDraw.Draw(bg)
    for y in range(SIZE):
        draw.line((0, y, SIZE, y), fill=lerp(top, bottom, y / SIZE) + (255,))

    # Soft teal glow behind the ports.
    glow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    gdraw = ImageDraw.Draw(glow)
    gdraw.ellipse((250, 290, 790, 830), fill=(45, 212, 191, 36))
    glow = glow.filter(ImageFilter.GaussianBlur(110))
    bg.alpha_composite(glow)

    icon = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    icon.paste(bg, (0, 0), mask)

    d = ImageDraw.Draw(icon)
    d.rounded_rectangle((0, 0, SIZE - 1, SIZE - 1), radius=224, outline=(40, 70, 80, 220), width=10)

    colors = [(45, 212, 191), (56, 189, 248), (100, 116, 139)]
    ys = [292, 470, 648]
    bar_h = 88
    for y, color in zip(ys, colors):
        # Slot.
        d.rounded_rectangle((236, y, 690, y + bar_h), radius=bar_h // 2, fill=color + (255,))
        # Status light with a dark socket centre.
        d.ellipse((756, y + 8, 756 + bar_h - 16, y + bar_h - 8), fill=color + (255,))
        d.ellipse((776, y + 26, 776 + bar_h - 52, y + bar_h - 26), fill=(8, 16, 24, 255))

    # Small top accent notch.
    d.rounded_rectangle((236, 196, 470, 220), radius=12, fill=(148, 163, 184, 210))

    icon.save(os.path.join(ASSETS, "icon.png"))
    icon.resize((256, 256), Image.LANCZOS).save(os.path.join(ASSETS, "icon-256.png"))

    icon.save(
        os.path.join(ASSETS, "app.ico"),
        sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print("Wrote Assets/app.ico, Assets/icon.png, Assets/icon-256.png")


if __name__ == "__main__":
    main()
