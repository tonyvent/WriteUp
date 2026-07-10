# WriteUp — Architecture

WriteUp is a Windows desktop app (C# / .NET 8 / WPF). You record what you do on
screen; every click, keystroke, scroll and pan becomes an editable **step** with
an annotated screenshot, and the steps render into a branded **write-up** you can
export to PDF, Word, or HTML.

The solution is `app/WriteUp.sln`; all source lives under `app/WriteUp/`.

## Layout

```
app/WriteUp/
├─ App.xaml(.cs)              Application entry + shared styles/brushes (the palette)
├─ MainWindow.xaml(.cs)       The main window: document details, record controls,
│  ├─ MainWindow.Export.cs      step list, live preview. Split into partials by area:
│  └─ MainWindow.Tour.cs         .Export.cs (Save-As/export), .Tour.cs (guided tour)
├─ CompactBar.xaml(.cs)       Small movable "recording" bar shown while minimized
├─ SettingsWindow.xaml(.cs)   Settings dialog (tour, sessions, screenshot size, feedback)
├─ AnnotationEditorWindow.*   Post-capture image editor (arrows/boxes/callouts/blur)
├─ MainViewModel.cs           Bound UI state: Steps, Meta, IsRecording, timers, flags
├─ PathToImageConverter.cs    Binds a file path to a fresh (uncached) thumbnail
├─ Models/                    Plain data (no behaviour)
│  ├─ Step.cs                   one recorded action (kind, caption, image paths, context)
│  ├─ SessionMeta.cs            document front-matter (title, author, department…)
│  ├─ Annotation.cs            one drawn mark on a screenshot
│  ├─ AppSettings.cs            persisted preferences
│  └─ FeedbackReport.cs         a user-submitted bug report / feedback item
└─ Services/                  The logic. No WPF windows here (except where noted).
   ├─ Recorder.cs               input events → ordered Steps (the core engine)
   ├─ InputHook.cs              low-level global mouse/keyboard hooks
   ├─ NativeMethods.cs          all Win32 P/Invoke in one place
   ├─ WindowTracker.cs          foreground window → generic app / surface name
   ├─ UiaInspector.cs           UI Automation: control + container under the cursor
   ├─ ScreenCapturer.cs         monitor capture + pointer/zoom-inset annotation
   ├─ ImageLoad.cs              shared cache-busting BitmapImage loader
   ├─ AnnotationStore.cs        load/save/burn annotations onto a screenshot file
   ├─ ReportWriter.cs           Markdown + self-contained HTML rendering
   ├─ FlowReport.cs             the same report as a WPF FlowDocument (live preview)
   ├─ DocumentExporter.cs       PDF + RTF(Word) via PDFsharp/MigraDoc
   ├─ SessionStore.cs           save/reopen a session (session.json + screenshots)
   ├─ SessionCleanup.cs         purge/keep transient session folders
   ├─ SettingsStore.cs          load/save AppSettings under %AppData%\WriteUp
   ├─ Branding.cs               the embedded Dynamic Engineering logo + company name
   └─ FeedbackService.cs        stores feedback locally (pluggable sink for later)
```

## How a recording becomes a report (data flow)

```
 global hooks        background worker              UI thread
┌────────────┐      ┌──────────────────┐          ┌───────────────────────┐
│ InputHook  │─────▶│ Recorder         │          │ MainViewModel.Steps   │
│ mouse/keys │ raw  │  • WindowTracker │  Step    │  (ObservableCollection)│
│  + wheel   │events│  • UiaInspector  │─────────▶│  → step list (edit)   │
└────────────┘      │  • ScreenCapturer│ (Dispatch)│  → FlowReport preview │
                    └──────────────────┘          └───────────┬───────────┘
                                                              │ export
                                        ┌─────────────────────┼───────────────┐
                                        ▼                     ▼               ▼
                                 ReportWriter.Html   DocumentExporter   (annotate:
                                   (.html)            .SavePdf/.SaveRtf   AnnotationEditor
                                                      (.pdf / .rtf)       → AnnotationStore)
```

1. **Capture.** `InputHook` installs low-level global hooks and raises clean
   mouse/keyboard/wheel events on the UI thread. `Recorder` enqueues them and a
   single background worker turns them into ordered `Step`s: it resolves the app
   and the specific surface (`WindowTracker` + `UiaInspector`), captures the
   monitor under the cursor with a pointer + optional zoom inset
   (`ScreenCapturer`), and writes a caption. Steps are raised back on the UI
   thread and land in `MainViewModel.Steps`.

   Non-actions are deliberately dropped: clicks/keys inside WriteUp itself, and
   clicks that only switch to another application.

2. **Review & edit.** The step list is bound to `MainViewModel.Steps`. Captions
   and section labels are editable; steps can be reordered, deleted, or opened in
   the `AnnotationEditorWindow`. A debounced `FlowReport` build renders the live
   preview; a debounced autosave writes `session.json` (`SessionStore`).

3. **Export.** `ReportWriter` (HTML), `DocumentExporter` (PDF/RTF), and
   `FlowReport` (preview) all render the same `SessionMeta` + `Step` list. The
   logo comes from `Branding` (embedded resource).

## Conventions

- **Coordinates are image pixels.** Screenshots and annotations use raw pixel
  coordinates so they're independent of DPI/monitor scaling and editor zoom.
- **Files are written atomically** (temp file, then move) and images are loaded
  cache-busted (`ImageLoad`) because the annotation editor rewrites files in place.
- **All Win32 interop lives in `NativeMethods`.** UI Automation is best-effort
  and always guarded with a timeout so a slow app never blocks recording.
- **`Services/` has no window dependencies** and is where the behaviour lives;
  the windows are thin and mostly wire events to services.
