# WriteUp — Windows desktop app (WPF)

A real, shareable Windows app that records what you do — every click gets an
annotated screenshot, what you type becomes a step — and turns it into a clean,
branded write-up. It shows a **live preview** of the report as you go and exports
to **PDF, Word, HTML, and Markdown**. Built in **C# / .NET 8 WPF**, with a single
small dependency (the MIT-licensed PDFsharp/MigraDoc) used only for PDF/Word.

## Open & run in Visual Studio
1. Install **Visual Studio 2022** (17.8+) with the **.NET desktop development**
   workload (includes the .NET 8 SDK).
2. Open `app/WriteUp.sln`.
3. Press **F5** (Debug) or **Ctrl+F5** (Run without debugging).

The first build restores one NuGet package (`PDFsharp-MigraDoc-WPF`), so an
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

or in Visual Studio: right-click the **WriteUp** project → **Publish** → choose the
**SingleFile** profile → **Publish**. Either way the result is one file:

```
app\WriteUp\bin\Release\net8.0-windows\win-x64\publish\WriteUp.exe
```

Copy just that **`WriteUp.exe`** to the shared folder — that's the whole app. The
logo and icon are embedded, so there are no loose files to copy and no runtime to
install (it's ~150 MB because the .NET runtime is bundled inside).

> Smaller alternative: if every PC already has the **.NET 8 Desktop Runtime**
> installed, you can instead publish framework-dependent
> (`--self-contained false`) for a much smaller exe.

A normal **Debug/Release build** (the `bin\…\net8.0-windows\` folder) is *not*
copy-one-file — its `WriteUp.exe` is just a launcher that needs the sibling DLLs
and an installed .NET runtime. Use the single-file publish above for sharing.

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
