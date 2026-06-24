"""Tests for the report generator (no display / input libs required)."""
from pathlib import Path

import pytest

from process_scribe.models import Event, Session, DocumentInfo
from process_scribe import writer


def _sample_session(tmp_path: Path) -> Session:
    s = Session(name="extrude-a-boss", started="2026-06-24T10:00:00", folder=tmp_path)
    s.meta = DocumentInfo(
        author="Jordan Lee",
        company="Acme Manufacturing",
        department="Mechanical Design",
        revision="1.0",
    )
    s.events = [
        Event(1, "window", "2026-06-24T10:00:01", window="Part1 - Autodesk Fusion",
              app="Autodesk Fusion"),
        Event(2, "click", "2026-06-24T10:00:02", app="Autodesk Fusion",
              button="left", x=120, y=80),
        Event(3, "type", "2026-06-24T10:00:03", app="Autodesk Fusion", text="25 mm"),
        Event(4, "key", "2026-06-24T10:00:04", text="enter"),
        Event(5, "window", "2026-06-24T10:00:05", window="Docs - Chrome",
              app="Chrome"),
        Event(6, "note", "2026-06-24T10:00:06", text="Cross-check the dimension."),
    ]
    return s


def test_markdown_structure(tmp_path):
    md = writer.to_markdown(_sample_session(tmp_path))
    assert "# Extrude A Boss" in md
    assert "## In Autodesk Fusion" in md
    assert "## In Chrome" in md
    assert "Type `25 mm`." in md
    assert "Press **Enter**." in md
    # Window markers are headings, not numbered steps.
    assert "**1.**" in md and "**4.**" in md
    assert "**5.**" not in md  # only 4 real steps


def test_html_is_standalone(tmp_path):
    h = writer.to_html(_sample_session(tmp_path))
    assert h.startswith("<!doctype html>")
    assert "Extrude A Boss" in h
    assert "<code>25 mm</code>" in h
    assert "<strong>Enter</strong>" in h


def test_describe_click_variants(tmp_path):
    left = Event(1, "click", "t", app="AutoCAD", button="left")
    right = Event(2, "click", "t", app="AutoCAD", button="right")
    assert writer.describe(left).startswith("Click")
    assert writer.describe(right).startswith("Right-click")


def test_generate_writes_files(tmp_path):
    session = _sample_session(tmp_path)
    session.folder.mkdir(parents=True, exist_ok=True)
    paths = writer.generate(session, fmt="all")
    names = {p.name for p in paths}
    # md + html are always produced; docx/pdf depend on optional deps.
    assert {"report.md", "report.html"} <= names
    for p in paths:
        assert p.exists() and p.stat().st_size > 0


def test_pdf_export(tmp_path):
    pytest.importorskip("fpdf")
    session = _sample_session(tmp_path)
    session.folder.mkdir(parents=True, exist_ok=True)
    paths = writer.generate(session, fmt="pdf")
    assert len(paths) == 1 and paths[0].name == "report.pdf"
    assert paths[0].read_bytes().startswith(b"%PDF")


def test_docx_export(tmp_path):
    pytest.importorskip("docx")
    session = _sample_session(tmp_path)
    session.folder.mkdir(parents=True, exist_ok=True)
    paths = writer.generate(session, fmt="docx")
    assert len(paths) == 1 and paths[0].name == "report.docx"
    assert paths[0].stat().st_size > 0


def test_explicit_missing_dep_raises(monkeypatch, tmp_path):
    # If fpdf isn't importable, an explicit pdf request should raise ImportError.
    import builtins

    real_import = builtins.__import__

    def fake(name, *a, **k):
        if name == "fpdf" or name.startswith("fpdf."):
            raise ImportError("no fpdf")
        return real_import(name, *a, **k)

    monkeypatch.setattr(builtins, "__import__", fake)
    session = _sample_session(tmp_path)
    session.folder.mkdir(parents=True, exist_ok=True)
    with pytest.raises(ImportError):
        writer.generate(session, fmt="pdf")


def test_header_in_markdown(tmp_path):
    md = writer.to_markdown(_sample_session(tmp_path))
    assert "**Acme Manufacturing** — Mechanical Design" in md
    assert "**Author:** Jordan Lee" in md
    assert "**Revision:** 1.0" in md
    # Date falls back to the recorded date (date part only).
    assert "**Date:** 2026-06-24" in md


def test_header_in_html(tmp_path):
    h = writer.to_html(_sample_session(tmp_path))
    assert "Acme Manufacturing" in h
    assert "class='brandbar'" in h
    assert "Jordan Lee" in h


def test_title_override(tmp_path):
    s = _sample_session(tmp_path)
    s.meta.title = "How to Extrude a Boss (Fusion 360)"
    assert "# How to Extrude a Boss (Fusion 360)" in writer.to_markdown(s)


def test_meta_merge_precedence():
    cli = DocumentInfo(author="CLI Author")
    saved = DocumentInfo(author="Saved Author", company="Saved Co")
    defaults = DocumentInfo(company="Default Co", department="Default Dept")
    result = cli.merged(lower=saved.merged(lower=defaults))
    assert result.author == "CLI Author"      # CLI wins
    assert result.company == "Saved Co"        # saved beats default
    assert result.department == "Default Dept"  # default fills the gap
