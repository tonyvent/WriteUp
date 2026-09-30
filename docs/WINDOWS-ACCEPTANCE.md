# WriteUp 0.5.0 Windows acceptance checks

Run on Windows 10/11 with the .NET 8 desktop runtime (or a self-contained publish).
These are manual hardware/UI checks, not claims of completed validation.

1. **Narration timing:** start recording, enable the microphone, speak context
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
