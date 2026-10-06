# WriteUp 0.5.1 Windows acceptance checks

Run on Windows 10/11 with the .NET 8 desktop runtime (or a self-contained publish).
These are manual hardware/UI checks, not claims of completed validation.

1. **Narration timing:** select and test the microphone/language in Settings, then start recording
   with automatic narration enabled and speak context
   before a click, click three controls while narrating, then stop mid-phrase.
   Confirm text is associated with the action at phrase start and final results
   are saved. Repeat using the compact bar. Test no microphone, denied access,
   and no installed recognizer: recording should remain usable with a visible
   explanation. Review recognition accuracy with finance/payroll vocabulary.
2. **Action descriptions:** use Edge on the guide that previously produced the
   repeated “Carmine field” caption. Record links, buttons, tabs, dropdowns and
   actual text inputs. Verify a document/page name is never reported as a field.
   Check right-click, double-click and clicks that activate another application.
3. **Mixed DPI:** connect displays at 100%, 150% and 200% scaling, including one
   left of or above the primary display. Click near each screen's corners and
   move an app between screens. Confirm the correct monitor, pointer, target
   outline, zoom inset, screenshot aspect ratio and annotation alignment.
4. **Context capture:** Alt+Tab between two applications and between two windows
   of the same application, leaving each active for at least one second without
   clicking. Confirm a context step for each. Use Ctrl+Alt+S for a screen change
   without a window/title change, and Ctrl+Alt+A to capture a side-by-side
   comparison. Check rapid input then Stop: the final typed action is retained.
5. **Shared screenshots:** record three near-identical screens. In Edit, have
   steps 2 and 3 reference step 1's screenshot; add 1/2/3 and A/B/C image markers
   and matching step labels. Confirm one image in every export and correct links.
   Delete the original image-owning step: a surviving instruction must retain
   that image. Undo deletion, reorder instructions, save and reopen.
6. **Editor:** add a text-only step, nested levels, bold, italic, combined
   bold/italic, line breaks, a web link and a step reference. Save/reopen and
   compare the preview, HTML, RTF opened in Word and PDF. Test step references
   after reordering or deleting their target; missing targets should be visible.
7. **Recovery:** edit, delete and reorder steps, then undo/redo; make a new edit
   after undo and confirm redo is cleared. In the image editor draw, resize,
   move, rename and delete a mark; undo/redo each. Reset then Cancel must leave
   the saved image unchanged. Reset then Save must remove editable marks while
   keeping previously saved redactions permanent.
8. **Persistence:** open a version-1 session; edit and save it, move the entire
   session folder, reopen it and export. Continue recording, stop, close and
   reopen again. Check images, zoom state, notes, nesting and links.

9. **Recording preferences:** disable typing but keep clicks; confirm typed text
   is omitted and click screenshots still arrive. Repeat with clicks disabled,
   scroll disabled and automatic window-change capture disabled. Confirm settings
   survive restart. Capture buttons and hotkeys should still support manual shots.
10. **Layouts:** resize Settings, main, step editor and annotation editor to their
    minimum sizes; expand feedback in Settings, use long context names and long
    transcripts, and scroll. Check Save/Cancel, start/stop, microphone controls and
    all annotation tools remain reachable and do not overlap text. Repeat at
    150% and 200% display scaling.


## 0.5.2 Microsoft Azure Speech acceptance

Use Settings > Microphone and narration > Microsoft Azure Speech (cloud).
Expand Azure Speech connection and enter the region and key of an Azure Speech
resource, language code (for example en-US), and semicolon-separated vocabulary
hints. The key is encrypted for the current Windows account using DPAPI.
Audio is sent to Azure; internet access and an Azure resource are required, and
usage charges may apply. Windows dictation remains available without Azure.
This integrates Microsoft's public Speech SDK, not Teams' internal configuration;
identical Teams accuracy is not guaranteed.

- [ ] Provider, microphone, language and vocabulary persist after restarting.
- [ ] Test microphone shows a live level, partial text, then finalized text.
- [ ] A normal sentence with short pauses remains understandable and complete.
- [ ] Compare the same spoken script and microphone with Teams, including office terminology.
- [ ] Narration captures why the action is needed and what the reader should check.
- [ ] Narration before/after clicking attaches to the correct action or screenshot.
- [ ] Stop while speaking: final buffered narration is retained, or an explicit timeout appears.
- [ ] Wrong key/region, lost internet and disconnected microphone produce useful status; screenshots continue.
- [ ] Restart narration after a failure or completed microphone test.
- [ ] Windows dictation still works with cloud provider disabled.
- [ ] Correct button/field descriptions replace repeated incorrect labels such as Carmine.
- [ ] Multiple instructions can share a screenshot and survive save/reopen/export.
- [ ] Mouse clicks, typing, scroll and window-change capture settings work individually.
- [ ] Start/stop and manual screenshot shortcuts work.
- [ ] All windows remain readable with no overlapping controls at 100%, 125%, 150% and 200% scaling.
- [ ] Exported instructions and narration preserve the spoken business meaning.

The Windows workflow publishes a downloadable WriteUp-0.5.2-Windows-x64 artifact
only after build and regression checks pass. No live Azure accuracy test is run in
CI; test your own microphone and Speech resource before relying on a guide.
