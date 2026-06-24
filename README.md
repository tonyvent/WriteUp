# ProcessScribe

Record what you do in **any** application — CAD, a browser, a terminal,
anything — and automatically get a clean, step-by-step write-up with
annotated screenshots.

It works like a souped-up version of Windows' old *Steps Recorder*: as you
work, ProcessScribe quietly captures your clicks, the text you type, and the
app you're in, marking each screenshot where you clicked. When you stop, it
turns that trail into a Markdown / HTML / Word document you can hand to a
teammate as a tutorial or standard operating procedure.

Built for people who do complicated multi-step processes (Fusion, AutoCAD,
Inventor, Revit, SolidWorks, etc.) and are tired of writing the docs by hand.

> **Two ways to use ProcessScribe**
> - **Windows desktop app (recommended)** — a real point-and-click app with a
>   Start/Stop button, live editable steps, and one-click export. Open
>   [`app/ProcessScribe.sln`](app/) in Visual Studio 2022, or see
>   [`app/README.md`](app/README.md). Built in C# / .NET 8 WPF with no NuGet
>   dependencies, and easy to publish as a single shareable `.exe`.
> - **Python command-line tool** — the original cross-platform CLI, documented
>   below, with native PDF and Word export.

---

## Features

- **Records across any program** via global mouse + keyboard hooks.
- **Annotated screenshots** — a target ring is drawn exactly where you clicked.
- **Groups steps by application** automatically (e.g. "In Autodesk Fusion", "In Chrome").
- **Smart text batching** — runs of typing become a single readable step.
- **Hotkeys** to stop, add a note, or drop a checkpoint without leaving your work.
- **Multiple outputs** — Markdown, a standalone HTML report, an editable **Word**
  document, and a print-ready **PDF**.
- **Title block & branding** — add a title, subtitle, author, date, revision, company
  name, department, and a logo to every report.
- **Re-generate any time** — recordings are saved as plain JSON + PNGs, so you can
  edit the notes and rebuild the report.

See [`examples/extrude-a-boss/`](examples/extrude-a-boss/) for a sample session
and its generated `report.html` / `report.md`.

---

## Install

Requires Python 3.9+.

```bash
git clone <your-repo-url> process-scribe
cd process-scribe
python -m venv .venv
# Windows:  .venv\Scripts\activate
# macOS/Linux: source .venv/bin/activate
pip install -e .            # core (Markdown + HTML)
pip install -e ".[export]"  # add Word (.docx) and PDF export
```

### Platform notes
- **Windows** — works out of the box.
- **macOS** — grant the terminal/Python *Accessibility* and *Screen Recording*
  permissions (System Settings → Privacy & Security).
- **Linux** — X11 is supported; install `xdotool` for window titles. Wayland
  restricts global input capture, so results vary.

---

## Usage

Start recording a process:

```bash
pscribe record --name "Extrude a boss in Fusion"
```

Then just do your work. Hotkeys while recording:

| Action       | Default hotkey   |
|--------------|------------------|
| Stop         | `Ctrl+Alt+S`     |
| Add a note   | `Ctrl+Alt+N`     |
| Checkpoint   | `Ctrl+Alt+C`     |

When you stop, the report is generated automatically and the location is
printed. By default (`--format all`) you get Markdown + HTML, plus Word and
PDF if the export extras are installed. Other commands:

```bash
pscribe list                      # show past sessions
pscribe report <session-folder>   # rebuild a report (e.g. after editing notes)
pscribe report <folder> --format pdf    # print-ready PDF
pscribe report <folder> --format docx   # editable Word document
pscribe report <folder> --embed   # self-contained HTML (images inlined)
```

### Print or edit
- **Print:** open `report.pdf` and print it, or open `report.html` and use your
  browser's *Print* (the HTML has print-optimized CSS with page breaks).
- **Edit:** open `report.docx` in Word / Google Docs / LibreOffice to tweak the
  wording, then export however you like. The raw `report.md` is also fully editable.

PDF page size defaults to US Letter; set `pdf_page_size: "A4"` in your config for A4.

Run without installing:

```bash
python -m process_scribe record --name "My process"
```

---

## Document info & branding

Every report can carry a title block with author, date, revision, and a
company header (with logo). Pass any of these to `record` or `report`:

```bash
pscribe report sessions/my-process \
  --title "How to Extrude a Boss in Fusion" \
  --subtitle "SOP for the base flange" \
  --author "Jordan Lee" \
  --company "Acme Manufacturing" \
  --department "Mechanical Design" \
  --date 2026-06-24 \
  --rev 1.0 \
  --logo ~/brand/acme-logo.png
```

| Flag           | Meaning                                            |
|----------------|----------------------------------------------------|
| `--title`      | Document title (defaults to the session name)      |
| `--subtitle`   | Short description under the title                  |
| `--author`     | Author name                                        |
| `--company`    | Company name shown in the header bar               |
| `--department` | Department / team                                  |
| `--date`       | Document date (defaults to the recorded date)      |
| `--rev`        | Revision label, e.g. `1.2`                         |
| `--logo`       | Path to a logo image (copied into the report)      |

Anything you set is saved with the session, so re-running `report` keeps it.
**Precedence:** CLI flag → value saved with the session → config default.

Set defaults once in your config so you never retype your company or logo:

```yaml
default_author: "Jordan Lee"
default_company: "Acme Manufacturing"
default_department: "Mechanical Design"
default_logo: "~/brand/acme-logo.png"
```

---

## Configuration

All settings have defaults; override them with a YAML file:

```bash
pscribe --config config.yaml record
```

See [`config.example.yaml`](config.example.yaml) for every option (output
folder, hotkeys, screenshot width, whether to capture scrolls, etc.).

---

## How it works

```
recorder.py  ─ captures clicks/keys/windows ─►  events.json + screenshots/*.png
                                                        │
writer.py    ◄──────────────────── reads ───────────────┘
   └─► report.md / report.html / report.docx / report.pdf
```

A session folder is fully self-describing: `events.json` plus a `screenshots/`
directory. Nothing is uploaded anywhere.

---

## Privacy

ProcessScribe records your **screen and keystrokes** while active. It captures
*everything* you type during a session — including anything sensitive — so:

- Only record while doing the specific process you want documented.
- Review the screenshots and `events.json` before sharing a report.
- Everything stays local on your machine; there is no network component.

---

## Roadmap

- Optional action *replay* from a recorded session.
- Region/window-only capture instead of full screen.
- Automatic blurring of fields marked sensitive.
- One-click export to Confluence / Notion.

## License

MIT — see [LICENSE](LICENSE).
