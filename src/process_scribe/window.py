"""Detect the currently focused window title and a friendly app name.

Works on Windows, macOS, and Linux with graceful fallbacks. If detection
fails for any reason we return empty strings rather than crashing the
recorder — context is nice-to-have, not essential.
"""
from __future__ import annotations

import shutil
import subprocess
import sys


def get_active_window() -> tuple[str, str]:
    """Return (window_title, app_name). Either may be empty."""
    try:
        if sys.platform.startswith("win"):
            title = _windows_title()
        elif sys.platform == "darwin":
            title, app = _macos()
            return title, app or guess_app_from_title(title)
        else:
            title = _linux_title()
    except Exception:
        return "", ""
    return title, guess_app_from_title(title)


def guess_app_from_title(title: str) -> str:
    """Heuristic: most apps render titles like 'Document - App Name'.

    AutoCAD: 'Drawing1.dwg - Autodesk AutoCAD 2025'
    Fusion:  'Untitled - Autodesk Fusion'
    VS Code: 'file.py - project - Visual Studio Code'
    """
    if not title:
        return ""
    parts = [p.strip() for p in title.split(" - ") if p.strip()]
    return parts[-1] if parts else title.strip()


# --------------------------------------------------------------------- Windows
def _windows_title() -> str:
    import ctypes

    user32 = ctypes.windll.user32  # type: ignore[attr-defined]
    hwnd = user32.GetForegroundWindow()
    length = user32.GetWindowTextLengthW(hwnd)
    buff = ctypes.create_unicode_buffer(length + 1)
    user32.GetWindowTextW(hwnd, buff, length + 1)
    return buff.value or ""


# ----------------------------------------------------------------------- macOS
def _macos() -> tuple[str, str]:
    app_script = (
        'tell application "System Events" to get name of first '
        "process whose frontmost is true"
    )
    app = _osascript(app_script)
    title_script = (
        'tell application "System Events" to tell (first process whose '
        "frontmost is true) to get name of front window"
    )
    title = _osascript(title_script)
    return title, app


def _osascript(script: str) -> str:
    res = subprocess.run(
        ["osascript", "-e", script], capture_output=True, text=True, timeout=2
    )
    return res.stdout.strip()


# ----------------------------------------------------------------------- Linux
def _linux_title() -> str:
    if shutil.which("xdotool"):
        res = subprocess.run(
            ["xdotool", "getactivewindow", "getwindowname"],
            capture_output=True,
            text=True,
            timeout=2,
        )
        if res.returncode == 0:
            return res.stdout.strip()
    if shutil.which("xprop"):
        # Fallback via xprop on the active window id.
        active = subprocess.run(
            ["xprop", "-root", "_NET_ACTIVE_WINDOW"],
            capture_output=True,
            text=True,
            timeout=2,
        ).stdout
        win_id = active.split()[-1] if active else ""
        if win_id.startswith("0x"):
            name = subprocess.run(
                ["xprop", "-id", win_id, "WM_NAME"],
                capture_output=True,
                text=True,
                timeout=2,
            ).stdout
            if '"' in name:
                return name.split('"', 1)[1].rsplit('"', 1)[0]
    return ""
