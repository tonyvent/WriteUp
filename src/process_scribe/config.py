"""User configuration with sensible defaults.

Loads an optional YAML file; everything has a default so the tool runs
out of the box with no config at all.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any


def default_output_dir() -> Path:
    return Path.home() / "ProcessScribe" / "sessions"


@dataclass
class Config:
    output_dir: Path = field(default_factory=default_output_dir)
    capture_on_click: bool = True
    capture_on_scroll: bool = False
    max_screenshot_width: int = 1600
    # How long a typed run can get before we flush it into its own step.
    type_flush_chars: int = 60
    # Hotkeys (pynput-style strings).
    stop_hotkey: str = "<ctrl>+<alt>+s"
    note_hotkey: str = "<ctrl>+<alt>+n"
    checkpoint_hotkey: str = "<ctrl>+<alt>+c"

    # Default document branding (used unless overridden per-report).
    default_author: str = ""
    default_company: str = ""
    default_department: str = ""
    default_logo: str = ""

    # PDF page size: "Letter" or "A4".
    pdf_page_size: str = "Letter"

    @classmethod
    def load(cls, path: str | Path | None = None) -> "Config":
        cfg = cls()
        if path is None:
            return cfg
        path = Path(path)
        if not path.exists():
            return cfg
        try:
            import yaml

            data: dict[str, Any] = yaml.safe_load(path.read_text()) or {}
        except Exception:
            return cfg
        for key, value in data.items():
            if hasattr(cfg, key):
                if key == "output_dir":
                    value = Path(value).expanduser()
                setattr(cfg, key, value)
        return cfg
