"""Command-line interface for ProcessScribe."""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from . import __version__
from .config import Config


def _add_meta_args(p: argparse.ArgumentParser) -> None:
    """Document title-block / branding flags shared by record and report."""
    g = p.add_argument_group("document info")
    g.add_argument("--title", help="Document title (defaults to the session name).")
    g.add_argument("--subtitle", help="Subtitle / short description.")
    g.add_argument("--author", help="Author name.")
    g.add_argument("--company", help="Company name for the header.")
    g.add_argument("--department", help="Department / team.")
    g.add_argument("--date", help="Document date (defaults to the recorded date).")
    g.add_argument("--revision", "--rev", dest="revision", help="Revision label, e.g. 1.2.")
    g.add_argument("--logo", help="Path to a logo image for the header.")


def _cli_meta(args):
    """DocumentInfo holding only what the user passed on the command line."""
    from .models import DocumentInfo

    return DocumentInfo(
        title=getattr(args, "title", None) or "",
        subtitle=getattr(args, "subtitle", None) or "",
        author=getattr(args, "author", None) or "",
        company=getattr(args, "company", None) or "",
        department=getattr(args, "department", None) or "",
        date=getattr(args, "date", None) or "",
        revision=getattr(args, "revision", None) or "",
        logo=getattr(args, "logo", None) or "",
    )


def _default_meta(config: Config):
    from .models import DocumentInfo

    return DocumentInfo(
        author=config.default_author,
        company=config.default_company,
        department=config.default_department,
        logo=config.default_logo,
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog="pscribe",
        description="Record what you do in any app and auto-generate a write-up.",
    )
    parser.add_argument("--version", action="version", version=f"%(prog)s {__version__}")
    parser.add_argument("--config", help="Path to a YAML config file.")
    sub = parser.add_subparsers(dest="command", required=True)

    rec = sub.add_parser("record", help="Start a recording session.")
    rec.add_argument("-n", "--name", help="Name for this process / session.")
    rec.add_argument("-o", "--out", help="Output directory for sessions.")
    rec.add_argument(
        "--format",
        default="all",
        choices=["all", "md", "html", "docx", "pdf", "none"],
        help="Report format(s) to generate when recording stops.",
    )
    rec.add_argument(
        "--embed", action="store_true", help="Embed images in the HTML report."
    )
    _add_meta_args(rec)

    rep = sub.add_parser("report", help="(Re)generate a report from a session folder.")
    rep.add_argument("session", help="Path to a session folder.")
    rep.add_argument(
        "--format", default="all", choices=["all", "md", "html", "docx", "pdf"]
    )
    rep.add_argument("--embed", action="store_true")
    _add_meta_args(rep)

    lst = sub.add_parser("list", help="List recorded sessions.")
    lst.add_argument("-o", "--out", help="Sessions directory to scan.")

    args = parser.parse_args(argv)
    config = Config.load(args.config)

    if args.command == "record":
        return _cmd_record(args, config)
    if args.command == "report":
        return _cmd_report(args, config)
    if args.command == "list":
        return _cmd_list(args, config)
    return 1


def _emit(session, args, config: Config) -> None:
    """Generate the requested report format(s), with helpful errors."""
    from . import writer

    try:
        paths = writer.generate(
            session,
            fmt=args.format,
            embed=args.embed,
            page_size=getattr(config, "pdf_page_size", "Letter"),
        )
    except ImportError:
        hint = {"docx": "python-docx", "pdf": "fpdf2"}.get(args.format, "")
        print(
            f"The '{args.format}' format needs an extra package. Install it with:\n"
            f"    pip install {hint}\n"
            f"or install everything with:  pip install \"process-scribe[export]\"",
            file=sys.stderr,
        )
        return
    if not paths:
        print(
            "No report written. For Word/PDF install the extras:\n"
            "    pip install \"process-scribe[export]\"",
            file=sys.stderr,
        )
    for p in paths:
        print(f"Wrote {p}")


def _cmd_record(args, config: Config) -> int:
    if args.out:
        config.output_dir = Path(args.out).expanduser()
    try:
        from .recorder import Recorder
    except ImportError as exc:  # pragma: no cover - depends on optional deps
        print(
            "Recording needs the input/capture libraries. Install them with:\n"
            "    pip install pynput mss Pillow\n"
            f"(import error: {exc})",
            file=sys.stderr,
        )
        return 2

    recorder = Recorder(name=args.name, config=config)
    recorder.session.meta = _cli_meta(args).merged(lower=_default_meta(config))
    print(f"Recording '{recorder.session.name}'.")
    print(f"  Stop:       {config.stop_hotkey}")
    print(f"  Add note:   {config.note_hotkey}")
    print(f"  Checkpoint: {config.checkpoint_hotkey}")
    print(f"  Saving to:  {recorder.session.folder}")
    try:
        session = recorder.start()
    except KeyboardInterrupt:
        recorder.stop()
        session = recorder.session
    print(f"\nCaptured {len(session.events)} events.")

    if args.format != "none":
        _emit(session, args, config)
    return 0


def _cmd_report(args, config: Config) -> int:
    from .models import Session

    session = Session.load(args.session)
    # Precedence: CLI flags > meta saved with the session > config defaults.
    base = session.meta.merged(lower=_default_meta(config))
    session.meta = _cli_meta(args).merged(lower=base)
    session.save()
    _emit(session, args, config)
    return 0


def _cmd_list(args, config: Config) -> int:
    from .models import Session

    base = Path(args.out).expanduser() if args.out else config.output_dir
    if not base.exists():
        print(f"No sessions directory yet at {base}")
        return 0
    folders = sorted(p for p in base.iterdir() if (p / "events.json").exists())
    if not folders:
        print(f"No sessions found in {base}")
        return 0
    for folder in folders:
        try:
            s = Session.load(folder)
            steps = sum(1 for e in s.events if e.kind != "window")
            print(f"{folder.name:40s}  {steps:4d} steps  {s.started}")
        except Exception:
            print(f"{folder.name:40s}  (unreadable)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
