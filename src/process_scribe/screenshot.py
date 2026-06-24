"""Screen capture with a click marker drawn at the point of action.

Uses mss for fast multi-monitor capture and Pillow to draw a target ring
where the user clicked, so each screenshot in the write-up clearly shows
*what* was clicked.
"""
from __future__ import annotations

from pathlib import Path
from typing import Optional


def capture(
    path: str | Path,
    click: Optional[tuple[int, int]] = None,
    max_width: int = 1600,
) -> Path:
    """Grab the full virtual screen, optionally mark a click, and save a PNG.

    `click` is the global (x, y) of the cursor in screen coordinates.
    The image is downscaled to `max_width` to keep reports lightweight.
    """
    import mss
    from PIL import Image

    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)

    with mss.mss() as sct:
        monitor = sct.monitors[0]  # index 0 = the full virtual screen
        raw = sct.grab(monitor)
        img = Image.frombytes("RGB", raw.size, raw.bgra, "raw", "BGRX")

    if click is not None:
        # Translate global coordinates into image-local coordinates.
        local = (click[0] - monitor["left"], click[1] - monitor["top"])
        _draw_marker(img, local)

    if img.width > max_width:
        ratio = max_width / img.width
        img = img.resize((max_width, int(img.height * ratio)), Image.LANCZOS)

    img.save(path, "PNG", optimize=True)
    return path


def _draw_marker(img, point: tuple[int, int]) -> None:
    """Draw a hard-to-miss double ring + crosshair at `point`."""
    from PIL import ImageDraw

    draw = ImageDraw.Draw(img, "RGBA")
    x, y = point
    accent = (255, 76, 31)  # orange-red
    glow = (255, 76, 31, 60)

    draw.ellipse([x - 26, y - 26, x + 26, y + 26], fill=glow)
    for r, w in ((22, 4), (12, 3)):
        draw.ellipse(
            [x - r, y - r, x + r, y + r],
            outline=accent + (255,),
            width=w,
        )
    draw.line([x - 30, y, x - 14, y], fill=accent, width=2)
    draw.line([x + 14, y, x + 30, y], fill=accent, width=2)
    draw.line([x, y - 30, x, y - 14], fill=accent, width=2)
    draw.line([x, y + 14, x, y + 30], fill=accent, width=2)
