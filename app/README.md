# WriteUp — Windows desktop app (WPF)

A real, shareable Windows app that records what you do — every click gets an
annotated screenshot, what you type becomes a step — and turns it into a clean,
branded write-up. It shows a **live preview** of the report as you go and exports
to **PDF, Word, HTML, and Markdown**. Built in **C# / .NET 8 WPF**, with PDFsharp/MigraDoc for document export, System.Speech for local transcription,
and NAudio for selecting a microphone.

## Open & run in Visual Studio
1. Install **Visual Studio 2022** (17.8+) with the **.NET desktop development**
   workload (includes the .NET 8 SDK).
2. Open `app/WriteUp.sln`.
3. Press **F5** (Debug) or **Ctrl+F5** (Run without debugging).

The first build restores the export, speech and microphone NuGet packages, so an
internet connection is needed the first time; after that it builds offline.

> Prefer the command line? From the `app/` folder:
> ```
> dotnet run --project WriteUp
> ```

## Share it as a single .exe

To put WriteUp on a shared drive so anyone can run it **without installing .NET**,
publish a self-contained single file. Easiest way — double-click:

```
app\publish-singlefile.cmd
```

It cleans its output folder, publishes, and lists what it produced. The result is
one file in a dedicated folder (kept separate from the build output on purpose):

```
app\WriteUp\publish\WriteUp.exe   <-- copy just this one file
```

Copy that **`WriteUp.exe`** to the shared folder — that's the whole app. The logo
and icon are embedded, so there are no loose files and no runtime to install
(it's ~150 MB because the .NET runtime is bundled inside). In Visual Studio you
can instead right-click the **WriteUp** project → **Publish** → **SingleFile**
profile → **Publish** (same output folder).

> ⚠️ **Don't copy from the `bin\` folders.** A normal build (and the
> `bin\Release\net8.0-windows\win-x64\` folder) always has `WriteUp.exe`
> surrounded by lots of DLLs — that exe is only a launcher and won't run on its
> own. The standalone single file is **only** the one in `app\WriteUp\publish\`
> produced by the script/profile above.

> Smaller alternative: if every PC already has the **.NET 8 Desktop Runtime**
> installed, publish framework-dependent (`--self-contained false`) instead for
> a much smaller exe.

## Build an installer (for download-and-install distribution)
One-time setup: install the free [Inno Setup 6](https://jrsoftware.org/isdl.php).
Then double-click **`app\make-installer.cmd`** — it publishes the single-file
exe and wraps it into `app\installer\output\WriteUp-Setup-<version>.exe`.

What recipients get: double-click, no admin rights needed (installs per-user),
Start Menu + optional desktop shortcut, an entry in Windows "Installed apps"
with a working uninstaller, and newer installers upgrade in place.

When you *don't* need it: if you're just dropping `WriteUp.exe` on the shared
drive, skip the installer — the single exe already runs anywhere.

Heads-up: the exe and installer are unsigned, so Windows SmartScreen may show
"Windows protected your PC" on first run — click "More info → Run anyway".
Code-signing certificates make that go away but cost money.

## How to use it
1. Fill in the **Document details** (title, author, company, logo, …) — optional,
   and remembered for next time.
2. Click **● Start recording** (or press **Ctrl+Alt+R** anywhere).
3. Switch to your CAD tool / browser / whatever and do the task. Each click is
   captured with a highlighted target ring; typed text is grouped into "Type …"
   steps; pressing the on-screen **＋ Add note** drops in a checkpoint.
4. Click **■ Stop** (or Ctrl+Alt+R again).
5. Watch the **report preview** on the right update as you work (toggle **Auto**
   off and use **⟳ Refresh** if you prefer manual). Edit any step's wording
   inline, delete the ones you don't need, then export with one click:
   **PDF** (print-ready), **Word**, **HTML**, or **Markdown**. Each export opens a
   **Save As** dialog so you can save anywhere — a shared drive, OneDrive, the
   desktop, wherever — not just the session folder. The app remembers your last
   export location for next time, and names the file from the report title.

### Annotate screenshots (arrows, boxes, callouts, blur/redact)
After recording, click **✎** on any step to open the image editor. Arrows,
boxes and text callouts stay editable across sessions (they re-render from a
backed-up original). **Blur** pixelates and **Redact** blacks out regions —
both are burned in permanently on Save, including into the backup, so no
un-redacted copy of that region remains on disk. Edits apply to the image as
currently shown (zoom inset on or off). Exports and the live preview pick up
annotations automatically.

### Sessions: auto-saved, reopenable, reorderable
Every session auto-saves a `session.json` in its folder (about a second after
each change, plus on Stop and on exit). **📂 Open…** reloads any past session —
steps, captions, section labels, zoom toggles, order and annotations all come
back. Image references are stored relative to the folder, so a session folder
can be moved or shared on a network drive. Reorder steps any time with **▲▼**.

Note: reopening requires "Delete session folders when the app closes" to be
OFF in ⚙ Settings (it now defaults to off). Turn it on to reclaim disk space
instead — sessions then vanish on exit, as before.

### Guided tour
A short walkthrough highlights each part of the UI on first launch. Replay it
or turn it off from **⚙ Settings** (top-right).

### Export formats
- **PDF** — native, print-ready, page-numbered. Built with PDFsharp/MigraDoc.
- **Word** — written as **`.rtf`**, which Word opens and edits natively; use
  *Save As → .docx* if you want the Word format. RTF is used so the app needs no
  Microsoft Office install and no heavy Open-XML tooling, while staying fully
  editable. (The companion Python tool in this repo emits true `.docx` if needed.)
- **HTML** — a single self-contained file (images embedded), great for sharing or
  printing to PDF from a browser.
- **Markdown** — plain `report.md` with the screenshots beside it.

Tip: leave **Always on top** checked so the panel floats over your other app.

## Share it with others (build a standalone .exe)
Produce a self-contained build that runs on any Windows 10/11 machine **without
installing .NET**:

```powershell
cd app
dotnet publish WriteUp -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The result is a single `WriteUp.exe` under
`WriteUp/bin/Release/net8.0-windows/win-x64/publish/`.
Zip that one file and send it — recipients just double-click to run.

For a smaller build that relies on the machine already having the .NET 8 Desktop
Runtime, drop `--self-contained` (and the single-file flags).

### Running from a shared drive
You can drop the published folder (or the single `WriteUp.exe`) on a shared
network drive and let people run it from there. A few things worth knowing:
- Each user's **settings and recorded sessions** live under *their own*
  `%AppData%\WriteUp`, not on the shared drive — so people don't collide.
- **Exports** go wherever each person picks in the Save As dialog (e.g. a shared
  "Procedures" folder on that same drive).
- The self-contained single-file build is the most portable; the first launch
  from a network path may be a touch slower as Windows caches it.

## Where things are saved
- Sessions (screenshots + exported reports): `%AppData%\WriteUp\sessions\…`
  (change this from the **Change…** button next to *Output*).
- Settings/branding defaults: `%AppData%\WriteUp\settings.json`.

## Privacy & permissions
- The app installs standard Windows low-level mouse/keyboard hooks **only while
  recording** to know when you click and what you type; it stops them the moment
  you press Stop. Nothing is sent anywhere — everything stays in local files.
- Because it reads global input, some endpoint-security tools may flag it the
  first time. It needs no admin rights for normal apps; to capture clicks inside
  an app that runs **as administrator**, run WriteUp as administrator too.

## Project layout
```
app/
  WriteUp.sln
  WriteUp/
    WriteUp.csproj        net8.0-windows, WPF, no NuGet
    app.manifest                per-monitor DPI awareness
    App.xaml(.cs)               theme, button styles
    MainWindow.xaml(.cs)        the window + all interactions
    MainViewModel.cs            bindable state (steps, status, meta, preview)
    Models/                     Step, SessionMeta, AppSettings
    Services/
      NativeMethods.cs          Win32 P/Invoke (hooks, hotkey, window)
      InputHook.cs              global mouse/keyboard hooks → events
      WindowTracker.cs          active window title + app name
      ScreenCapturer.cs         screen grab + target-ring marker
      Recorder.cs               events → ordered, captioned steps
      ReportWriter.cs           Markdown + self-contained HTML
      DocumentExporter.cs       native PDF + Word(.rtf) via PDFsharp/MigraDoc
      FlowReport.cs             FlowDocument builder for the live preview
      SettingsStore.cs          load/save settings under %AppData%
```

## Notes
- Typed-text capture is best-effort (it follows your keyboard layout). Every step
  caption is editable in the UI before export, so you can always fix wording.
- The preview is a faithful WPF rendering of the report; the exported PDF/Word may
  differ very slightly in pagination but matches content and styling.

## 0.5.0 — capture and editor enhancements

- **Narration:** narration starts with recording when enabled in Settings.
  Use the microphone button in the main window or compact bar to pause/restart it. Explain why you are performing each action.
  Recognized phrases are added to the notes of the step active when the phrase
  began; speech before the first action becomes a business-context step. Notes
  autosave and appear in every report format. Click again to stop dictation.
  This uses the installed Windows speech recognizer and selected microphone:
  install a speech language and allow desktop microphone access if unavailable.
  Recognition happens locally; WriteUp does not save audio or upload it.
- **Descriptions:** browser documents are no longer classified as text fields.
  Hit-test bounds are checked, small actionable parents of icon/label controls
  are preferred, and unknown targets use an honest highlighted-location caption.
  UI Automation remains best-effort, especially for custom-drawn controls.
- **Shared screenshots:** click **Edit** on a step and select the screenshot from
  another step. The report renders the image once and links the other instructions
  to it. Open the source image's **✎** editor and use **1 2 3** or **A B C** to
  place markers, then set each instruction's matching annotation label in Edit.
  Existing captures are retained so sharing can be undone. Consolidation is an
  author decision; similar screens are not automatically merged.
- **Editing:** Edit includes business-context notes with bold/italic formatting,
  web/email links and references to other steps, five levels of instruction
  hierarchy, screenshot selection, and text-only mode. **Add note** also works
  outside recording and adds a text-only instruction. Formatting is stored as a
  small Markdown vocabulary and rendered in HTML, PDF, Word-compatible RTF and
  the preview. The Windows app's Word output is still RTF, not native DOCX.
- **Undo/redo:** use **Undo step edit / Redo step edit** for description edits,
  deletions, reordering, nesting, screenshot sharing and note changes. Step
  history holds the latest 200 snapshots for the current editing session and
  resets when opening a session or completing a recording. Text editors also
  have their normal local undo. The image editor has separate Undo/Redo buttons
  and Ctrl+Z/Ctrl+Y for drawing, moving, resizing, editing labels, deleting and
  resetting marks. Reset is staged until Save; Cancel leaves the image intact.
  Previously saved blur/redaction remains permanent. Image history is local to
  the open image editor, not the step-history buttons.
- **Multiple monitors/windows:** captures now resolve native physical-pixel
  monitor geometry in an explicit per-monitor DPI context; negative display
  coordinates are supported. Foreground-window/title changes are checked every
  600 ms while recording, so switching windows without clicking can produce a
  context screenshot. Very brief switches can be missed. **Ctrl+Alt+S** captures
  the current application's monitor; **Ctrl+Alt+A** or **Capture all screens**
  captures the full virtual desktop for comparisons. These are desktop captures,
  so visible overlapping windows are included. Click captures remain on the
  clicked monitor and respect the existing maximum image-width setting.

Stopping, exporting or closing now drains queued capture events and the final
recognized speech before saving. Continuing a recording keeps its images in the
same session folder. Sessions saved by older versions can still be opened.

### Regression checks

```powershell
dotnet build app/WriteUp/WriteUp.csproj -c Release
dotnet run --project app/WriteUp.CoreTests -c Release
```

The core checks cover session backward compatibility and portability, undo/redo,
shared images, nested numbering, safe links, formatted HTML/Markdown and the
existing annotation enum values. GitHub Actions also builds on Windows.

Before distributing this version, run the [Windows acceptance checks](../docs/WINDOWS-ACCEPTANCE.md)
with a microphone and mixed-DPI monitors. A successful cross-build is not a live
capture or speech-recognition test.


## 0.5.1 — recording settings and live narration

Open **Settings → Microphone and narration** before recording:

1. Enable **Listen and transcribe automatically when recording starts** (on by default).
2. Select a microphone and installed Windows speech language.
3. Click **Test microphone**. Check that the meter moves and your words appear.
   If needed, use **Windows sound settings** to fix input volume or permissions.
4. Choose mouse-click, typing, scroll and window-change capture options in
   **Recording**, then Save and start recording.

During recording, tentative speech appears as **Hearing…**, while finalized
phrases are appended to the relevant step's notes and autosaved. The main window
and compact bar both show microphone state, audio level and transcription. Raw
audio is streamed in memory and not retained. Microphone failure is visible and
does not prevent screenshot recording. Input-hook failure now aborts startup
with an error instead of falsely displaying an active recording. Typing/scroll
bursts flush after an idle interval so they do not wait until Stop to appear.

Settings is resizable and scrollable with Save/Cancel kept outside the scrolling
content. Main-window headers, step actions, annotation tools and footer actions
use distinct rows or wrapping panels. The recording controls and live transcript
stay outside the step list's scrolling area.

Additional Windows checks:

```powershell
dotnet run --project app/WriteUp.WindowsTests -c Release
```

This opens an isolated target window, exercises the actual Windows input hooks
and screenshot capture, and checks visible control intersections at minimum
window sizes and in scrolled views. It requires an interactive Windows desktop.
It does not replace a real microphone test or physical mixed-DPI monitor testing.
