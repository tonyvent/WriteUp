"""Core data structures for a recording session.

Kept dependency-free on purpose: nothing here imports pynput / mss / Pillow,
so the report generator and tests can run on a machine with no display.
"""
from __future__ import annotations

import json
from dataclasses import dataclass, field, asdict
from pathlib import Path
from typing import Any, Optional

EVENTS_FILENAME = "events.json"
SHOTS_DIRNAME = "screenshots"


@dataclass
class Event:
    """A single recorded action."""

    index: int
    kind: str  # click | type | scroll | key | note | window
    timestamp: str  # ISO-8601
    window: str = ""  # active window title at the time
    app: str = ""  # best-guess application name
    text: str = ""  # typed text, note body, or key name
    button: str = ""  # mouse button for clicks
    x: Optional[int] = None
    y: Optional[int] = None
    screenshot: Optional[str] = None  # path relative to the session folder

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> "Event":
        known = {f for f in cls.__dataclass_fields__}  # type: ignore[attr-defined]
        return cls(**{k: v for k, v in d.items() if k in known})


@dataclass
class DocumentInfo:
    """Front-matter for the generated write-up (title block + branding)."""

    title: str = ""
    subtitle: str = ""
    author: str = ""
    company: str = ""
    department: str = ""
    date: str = ""  # document date; defaults to the recorded date
    revision: str = ""
    logo: str = ""  # path to a logo image (copied into the session on render)

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> "DocumentInfo":
        known = {f for f in cls.__dataclass_fields__}  # type: ignore[attr-defined]
        return cls(**{k: v for k, v in (d or {}).items() if k in known})

    def merged(self, *, lower: "DocumentInfo") -> "DocumentInfo":
        """Return a copy where empty fields fall back to `lower`."""
        out = DocumentInfo()
        for f in self.__dataclass_fields__:  # type: ignore[attr-defined]
            value = getattr(self, f) or getattr(lower, f)
            setattr(out, f, value)
        return out


@dataclass
class Session:
    """An ordered collection of events plus metadata."""

    name: str
    started: str
    folder: Path
    events: list[Event] = field(default_factory=list)
    finished: Optional[str] = None
    meta: DocumentInfo = field(default_factory=DocumentInfo)

    # ---- convenience -------------------------------------------------
    @property
    def screenshots_dir(self) -> Path:
        return self.folder / SHOTS_DIRNAME

    def add(self, event: Event) -> None:
        self.events.append(event)

    # ---- persistence -------------------------------------------------
    def save(self) -> Path:
        self.folder.mkdir(parents=True, exist_ok=True)
        self.screenshots_dir.mkdir(parents=True, exist_ok=True)
        payload = {
            "name": self.name,
            "started": self.started,
            "finished": self.finished,
            "meta": self.meta.to_dict(),
            "events": [e.to_dict() for e in self.events],
        }
        out = self.folder / EVENTS_FILENAME
        out.write_text(json.dumps(payload, indent=2), encoding="utf-8")
        return out

    @classmethod
    def load(cls, folder: str | Path) -> "Session":
        folder = Path(folder)
        data = json.loads((folder / EVENTS_FILENAME).read_text(encoding="utf-8"))
        session = cls(
            name=data.get("name", folder.name),
            started=data.get("started", ""),
            finished=data.get("finished"),
            folder=folder,
        )
        session.meta = DocumentInfo.from_dict(data.get("meta", {}))
        session.events = [Event.from_dict(e) for e in data.get("events", [])]
        return session
