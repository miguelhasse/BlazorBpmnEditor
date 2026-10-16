# Canvas navigation

How the canvas is moved, and what the host controls.

Blazor owns every public setting; the canvas derives its behaviour from the configuration snapshot
and keeps no competing defaults of its own. Nothing below is hard-coded on the TypeScript side.

`EditingOptions.EventEditPolicy` is serialized as `reject` or `autoAdjustInterrupting` in that same
snapshot. It defaults to `reject`; the alternate policy changes only a required interruption flag
and never substitutes an event trigger. Both the .NET and TypeScript config validators reject
unknown policy values.

## Pointer

| Gesture | Effect |
|---|---|
| Drag blank paper | Pan |
| Wheel | Pan vertically (`ViewportOptions.WheelMode`, default `Pan`) |
| Shift + wheel | Pan horizontally |
| Ctrl or Cmd + wheel | Zoom about the pointer — always, whatever `WheelMode` says |
| One finger | Pan |
| Two fingers | Pinch zoom about their midpoint, plus the midpoint's own translation |

A wheel event is what a trackpad sends for a two-finger scroll, and Ctrl+wheel is what it sends for
a pinch, so those two rules cover mouse and trackpad without sniffing for either. Wheel deltas are
normalised to pixels first: `deltaMode` reports lines in Firefox and pages on some mice, and
treating either as pixels sends the canvas across the screen on one notch.

The canvas is unbounded. Panning is a translate with no clamp, so there is no edge to reach and no
content extent to maintain; fit-to-content is the only operation that asks the paper what it drew.

`ViewportOptions.TouchEnabled` also decides whether `touch-action: none` is applied. It is set from
the accepted configuration rather than in the stylesheet, because suppressing the browser's own
scroll and pinch is only correct when the canvas is actually handling those gestures — hard-coding
it would trap a touch user on a canvas configured not to.

## Keyboard

Bindings come from `KeyboardOptions.Bindings`. The defaults:

| Keys | Action |
|---|---|
| `+`, `=` | Zoom in |
| `-` | Zoom out |
| `0` | Reset zoom to 100% |
| `F` | Fit the diagram to the view |
| Arrow keys | Pan by `KeyboardOptions.PanStep` (50 units) |
| `Shift + ?` | Show keyboard shortcuts |

Modifiers match **exactly**, not as a minimum, so `Ctrl+0` and `Ctrl+Shift+0` are distinct bindings
and neither shadows the other. A single printable key compares case-insensitively — `Shift+a`
reports `key: 'A'`, and a host that wrote `{ Key = "a", Shift = true }` means that. A named key such
as `ArrowLeft` compares exactly.

Two things the handler deliberately does not do:

- **It does not preventDefault until a binding matches.** Unbound keys reach the browser, so
  tabbing out of the canvas keeps working.
- **It ignores keystrokes aimed at text entry.** Typing `+` into a label editor or a property field
  must not zoom the canvas.

Listeners are on the canvas element rather than the window, so a page with two editors delivers a
shortcut to the one the user is in.

### The help surface

`BpmnShortcutHelp` lists the bindings the canvas was actually configured with, so it cannot
describe a key the canvas does not have — a hand-written help list is the first thing to go stale
when a host rebinds something. Several keys bound to one action share a row.

It renders its own overlay rather than using a Fluent UI dialog, because a service-backed dialog
needs the host to have added `FluentDialogProvider` to its layout and silently does nothing when it
has not. Opened by its bound key, by `BpmnToolbar`'s help control, or by calling
`BpmnEditor.ShowShortcutHelp()`.

Only commands the chrome owns cross interop. Panning and zooming are executed on the canvas
directly, because routing a key press through interop and back would make the canvas lag the
keyboard.

### Blank-paper drags

A plain drag pans. **Shift and drag** draws a rectangular selection instead — both gestures start on
blank paper with the primary button, so one has to yield, and panning is the one with alternatives:
the arrow keys, the scroll wheel and the toolbar all still pan. See `docs/editing.md`.

## Alignment guides

`SnappingOptions` turns on peer alignment: moving a shape snaps its edges and centres to those of
its siblings and draws a guide showing why. Distinct from `GridOptions.SnapEnabled`, which snaps to
fixed intervals — these snap to whatever the neighbours happen to be, which is what makes a diagram
look laid out rather than merely quantised. The default tolerance (6) is below the default grid step
(10) so the two do not fight.

Every combination of the moving shape's three edges against each peer's three is considered, and the
smallest correction wins; comparing only like with like would miss a shape's left edge lining up
with the centre of the one above it. The two axes resolve independently, so a shape can align
horizontally to one neighbour and vertically to another. Ties keep the earlier candidate, so the
result does not flicker as the pointer moves.

Guides are driven by `change:position` on the graph rather than by a drag handler. That makes them
correct for every way a shape can move — a drag, a keyboard nudge, a command, an undo — without each
of those having to remember to ask. Only siblings at the same containment level are candidates:
aligning to a shape in another Pool would be meaningless, and aligning to an ancestor would snap a
child to its container.

Element dragging and alignment guides run through the editing interactions. The Debug-only
`__moveElement` hook exercises the same position-change path in browser tests; it is not a public
host API. See [Editing](editing.md) for selection, movement and undo semantics.

## Accessibility

The accessible representation is **model-based**, not the currently mounted SVG:

- Editor, stencil and properties use named regions. The toolbar slot does not claim a second
  toolbar role around its content.
- The canvas container is focusable with `role="application"` and a configurable name.
- The paper wrapper is `aria-hidden="true"`. Drawn cells retain semantic names and
  `graphics-symbol` roles for inspection, but a screen reader does not announce the same diagram
  twice or lose off-screen elements when virtualization removes their SVG views.
- `BpmnAccessibleTree` exposes model elements as a `tree` of `treeitem` entries. The editor renders
  it by default (`ShowAccessibleTree = true`); standalone use can supply `Nodes` and callbacks.
- Expansion controls live outside the hidden paper wrapper, so their real buttons stay focusable
  and named. A visually hidden live region announces canvas changes.

Tree navigation is separate from canvas panning:

| Tree key | Action |
|---|---|
| Left / Right | Follow graph backward / forward |
| Up / Down | Previous / next graph choice |
| Home / End | First / last item |
| Enter | Activate the current element |
| `+` / `-` | Expand / collapse an expandable Sub-Process |

`BpmnGraphNavigation` decides destinations; the tree only maps keys and moves focus after render.
Keeping `ShowAccessibleTree` enabled preserves navigation independently of viewport culling.
See [Performance](performance.md#virtualization) for the reason SVG virtualization needs this
parallel representation.

## Seeing it run

`/editor` in the playground. `CanvasNavigationTests` drives all of the above in a real browser,
which is the only place wheel units, pointer capture and a live SVG guide layer behave as they
actually do.
