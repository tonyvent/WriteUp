"""The recording engine.

Listens to global mouse and keyboard events, captures annotated screenshots
on clicks, batches typed text into readable runs, tracks window/app changes,
and supports a few hotkeys (stop, note, checkpoint).

pynput callbacks run on their own threads, so all mutation of the session
goes through a lock.
"""
from __future__ import annotations

import threading
from datetime import datetime
from pathlib import Path
from typing import Optional

from .config import Config
from .models import Event, Session
from . import screenshot, window


def _now() -> str:
    return datetime.now().isoformat(timespec="seconds")


def _slug(name: str) -> str:
    keep = "-_."
    return "".join(c if c.isalnum() or c in keep else "-" for c in name).strip("-")


class Recorder:
    def __init__(self, name: str | None = None, config: Config | None = None):
        self.config = config or Config()
        stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
        session_name = name or f"session-{stamp}"
        folder = self.config.output_dir / f"{stamp}-{_slug(session_name)}"
        self.session = Session(name=session_name, started=_now(), folder=folder)

        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._counter = 0
        self._typed: list[str] = []
        self._last_window = ""
        self._mouse_listener = None
        self._keyboard_listener = None

    # ------------------------------------------------------------------ public
    def start(self) -> Session:
        """Begin recording. Blocks until a stop hotkey or stop() is called."""
        from pynput import keyboard, mouse

        self.session.save()  # create folders up front
        self._mouse_listener = mouse.Listener(
            on_click=self._on_click, on_scroll=self._on_scroll
        )
        hotkeys = self._build_hotkeys(keyboard)
        self._keyboard_listener = keyboard.Listener(
            on_press=lambda k: self._on_press(k, hotkeys),
            on_release=lambda k: hotkeys.release(k) if hotkeys else None,
        )
        self._mouse_listener.start()
        self._keyboard_listener.start()
        self._stop.wait()
        self._shutdown()
        return self.session

    def stop(self) -> None:
        self._stop.set()

    # ----------------------------------------------------------------- helpers
    def _build_hotkeys(self, keyboard):
        mapping = {
            self.config.stop_hotkey: self.stop,
            self.config.note_hotkey: self._add_note,
            self.config.checkpoint_hotkey: self._checkpoint,
        }
        return keyboard.HotKey and _HotKeyRouter(keyboard, mapping)

    def _next_index(self) -> int:
        self._counter += 1
        return self._counter

    def _context(self) -> tuple[str, str]:
        title, app = window.get_active_window()
        if title and title != self._last_window:
            self._record_window_change(title, app)
            self._last_window = title
        return title, app

    def _record_window_change(self, title: str, app: str) -> None:
        self.session.add(
            Event(
                index=self._next_index(),
                kind="window",
                timestamp=_now(),
                window=title,
                app=app,
            )
        )

    def _flush_text(self) -> None:
        if not self._typed:
            return
        text = "".join(self._typed)
        self._typed.clear()
        if not text.strip():
            return
        title, app = window.get_active_window()
        self.session.add(
            Event(
                index=self._next_index(),
                kind="type",
                timestamp=_now(),
                window=title,
                app=app,
                text=text,
            )
        )
        self.session.save()

    # ------------------------------------------------------------- mouse hooks
    def _on_click(self, x, y, button, pressed) -> None:
        if not pressed:
            return
        with self._lock:
            self._flush_text()
            title, app = self._context()
            shot_rel = None
            if self.config.capture_on_click:
                shot_rel = self._capture(x, y)
            self.session.add(
                Event(
                    index=self._next_index(),
                    kind="click",
                    timestamp=_now(),
                    window=title,
                    app=app,
                    button=str(button).replace("Button.", ""),
                    x=int(x),
                    y=int(y),
                    screenshot=shot_rel,
                )
            )
            self.session.save()

    def _on_scroll(self, x, y, dx, dy) -> None:
        if not self.config.capture_on_scroll:
            return
        with self._lock:
            title, app = self._context()
            self.session.add(
                Event(
                    index=self._next_index(),
                    kind="scroll",
                    timestamp=_now(),
                    window=title,
                    app=app,
                    text=f"dx={dx}, dy={dy}",
                    x=int(x),
                    y=int(y),
                )
            )

    # ---------------------------------------------------------- keyboard hooks
    def _on_press(self, key, hotkeys) -> None:
        if hotkeys:
            hotkeys.press(key)
        with self._lock:
            char = getattr(key, "char", None)
            if char is not None:
                self._typed.append(char)
                if len(self._typed) >= self.config.type_flush_chars:
                    self._flush_text()
                return
            # Special keys break a typed run.
            name = str(key).replace("Key.", "")
            if name == "space":
                self._typed.append(" ")
            elif name == "backspace":
                if self._typed:
                    self._typed.pop()
            elif name in {"enter", "tab", "esc"}:
                self._flush_text()
                self.session.add(
                    Event(
                        index=self._next_index(),
                        kind="key",
                        timestamp=_now(),
                        text=name,
                    )
                )

    # ------------------------------------------------------------- hotkey acts
    def _add_note(self) -> None:
        with self._lock:
            self._flush_text()
            shot = self._capture(None)
            self.session.add(
                Event(
                    index=self._next_index(),
                    kind="note",
                    timestamp=_now(),
                    text="(add your description here)",
                    screenshot=shot,
                )
            )
            self.session.save()

    def _checkpoint(self) -> None:
        with self._lock:
            self._flush_text()
            title, app = self._context()
            shot = self._capture(None)
            self.session.add(
                Event(
                    index=self._next_index(),
                    kind="note",
                    timestamp=_now(),
                    window=title,
                    app=app,
                    text="Checkpoint",
                    screenshot=shot,
                )
            )
            self.session.save()

    # ------------------------------------------------------------------ shared
    def _capture(self, point) -> Optional[str]:
        rel = Path("screenshots") / f"{self._counter + 1:04d}.png"
        try:
            screenshot.capture(
                self.session.folder / rel,
                click=point if point and point[0] is not None else None,
                max_width=self.config.max_screenshot_width,
            )
            return str(rel).replace("\\", "/")
        except Exception:
            return None

    def _shutdown(self) -> None:
        if self._mouse_listener:
            self._mouse_listener.stop()
        if self._keyboard_listener:
            self._keyboard_listener.stop()
        with self._lock:
            self._flush_text()
            self.session.finished = _now()
            self.session.save()


class _HotKeyRouter:
    """Thin wrapper that routes multiple global hotkeys to callbacks."""

    def __init__(self, keyboard, mapping: dict):
        self._keyboard = keyboard
        self._hotkeys = []
        for combo, callback in mapping.items():
            hk = keyboard.HotKey(keyboard.HotKey.parse(combo), callback)
            self._hotkeys.append(hk)

    def _canonical(self, key):
        # Listener.canonical normalises modifier keys for HotKey matching.
        return key

    def press(self, key):
        for hk in self._hotkeys:
            hk.press(key)

    def release(self, key):
        for hk in self._hotkeys:
            hk.release(key)
