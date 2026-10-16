# Performance budget

![Managed editor render path](../assets/diagrams/render-state.svg) budget

The benchmark diagram is `playground/Bpmn.Demo/wwwroot/samples/large-virtualization-stress.bpmn`:
**602 shapes and 601 connectors** spanning 5,400 × 2,980 model units. It is the fixture the plan
names for M7 and the one every number below was measured on.

Measured through the demo in headless Chromium at 1440 × 900, with frame times sampled inside the
page during real gestures — a pointer drag for pan, `Ctrl`+wheel for zoom. Timing a script that
calls into JointJS directly would leave out layout, paint and compositing, which is where a diagram
this size actually spends its time.

## The budget

| | Budget | Measured |
|---|---|---|
| Pan, median frame | ≤ 16.7 ms (60 fps) | 16.7 ms |
| Pan, 95th percentile frame | ≤ 17 ms | 16.8 ms |
| Zoom, median frame | ≤ 16.7 ms | 16.7 ms |
| Zoom, 95th percentile frame | ≤ 70 ms | 66.7 ms |
| SVG nodes in the document | ≤ 2,500 | 1,593 |
| First render | ≤ 3.5 s | 3.0 s |

**Pan holds 60 fps.** The 95th-percentile frame is one frame time, so the hitches are in the last
few percent.

**Zoom does not, and the budget says so.** Its top 5% of frames cost about four frame times. This
is stated rather than hidden because it is what the diagram costs today: a scale change invalidates
every mounted view at once, and that work is not divisible the way a pan's is. Raising this is the
next piece of performance work, not something the current numbers pretend is done.

## Virtualization

Only what is on screen, plus a full viewport of margin in each direction, is in the DOM. On this
diagram that is **1,593 SVG nodes instead of 8,433** — an 81% reduction — and about 240 mounted
cells instead of 1,203.

Two things were needed to make it a win rather than a loss, and both were found by measuring:

**A connector is judged by its endpoints, not by its own bounding box.** `getBBox()` on a link
computes its route. With six hundred of them, the visibility pass cost more than the drawing it was
meant to avoid: first render went from 3.4 s to 7.2 s and the worst zoom frame from 117 ms to
233 ms. Judging a link by the box spanning its two ends — inflated by the clearance an orthogonal
route takes when it steps around something — is a comparison of numbers already in the model.

**The pass runs when it can change the answer, not on every transform.** Cells are mounted for a
full viewport in each direction, and the pass re-runs once the viewport has moved half of that,
which leaves half a screen of already-built content ahead of the pan in every direction.

Against no virtualization at all, measured over three runs each: first render 3.0 s against 3.4 s,
frame times indistinguishable, DOM 81% smaller. It is a win on memory and on load, and neutral on
frames — the frame cost of this diagram is not the size of its DOM.

### What made it safe

Culling shapes is only acceptable because the SVG is no longer the accessible representation. A
screen reader walks `BpmnAccessibleTree`, which is built from the model and lists every element
whether or not the paper has drawn it. Doing this while the drawn cells were still what assistive
technology read would have hidden most of the diagram from anyone using one.

## Reproducing

Run the demo and drive it with the session's Playwright console driver:

```
dotnet run --project playground/Bpmn.Demo -c Release --urls http://localhost:5601
dotnet bin/Debug/net10.0/driver.dll --bench http://localhost:5601 large-virtualization-stress.bpmn
```

Frame counts vary between runs by roughly 15%; first render and DOM size do not. Compare medians
and percentiles across at least three runs, never single numbers — the first culling attempt looked
like a 2× regression and the second like a win, and both readings were the same code measured once.

## Micro-benchmarks

Model-level costs, with the numbers measured on the 600-shape stress fixture (one dev machine;
compare relative, not absolute):

```
cd src/Bpmn.Scripts && npm run bench                           # TS: renderDocument, scans, drop resolution
node src/Bpmn.Scripts/tools/perf-bench.mjs http://localhost:5601   # browser: first render, pan/zoom frames
dotnet run -c Release --project tests/Bpmn.Benchmarks -- --filter "*"   # C#: read, write, a11y graph, move
```

| Path | Measured |
|---|---|
| TS `renderDocument` (600 shapes) | ~8.7 ms |
| TS `getElements()` + `getBBox` scan | ~0.013 ms |
| TS `ownerUnder` / `targetAt` | ~0.025 ms |
| C# `BpmnXmlReader.Read` | ~7.5 ms, 4.8 MB |
| C# `BpmnXmlWriter.Write` (canonical) | ~15 ms, 7.5 MB |
| C# `BpmnAccessibleGraph.For` | ~0.42 ms |
| C# `MoveShapesCommand` (20 shapes) | ~0.9 ms |

Takeaway: whole-graph scans cost tens of microseconds at this size, so a shared scene index or
per-gesture caches would be speculative. The remaining cost is DOM work (zoom p95) and XML
serialization allocations.

## Interpreting benchmark results

The figures above are historical measurements, not machine-independent acceptance thresholds.
For a repeatable managed-code baseline, see [Bpmn.Benchmarks](../tests/Bpmn.Benchmarks/README.md):
it separates setup from XML read/write, accessible-graph construction and a twenty-shape command,
and records allocations with BenchmarkDotNet's memory diagnoser.

These micro-benchmarks do not measure JSON serialization, property-panel rendering, JS interop,
SVG paint or frame pacing. Run the browser harness for those canvas costs; an improvement in
managed XML allocations is not evidence of smoother zoom. Use the same fixture, configuration
and machine when comparing before/after runs.


## Managed editor state renders

`BpmnEditorComponentBase.ShouldRenderOnStateChange(previous, current)` is an opt-in
state-cascade filter; its default returns `true`. Core presentation bases compare the immutable
slices their markup consumes, shared by both kits. A filter is consulted only when a newly
assigned State is the sole changed parameter (other values retain reference identity). First
render, other parameter updates, local `StateHasChanged`, events, asynchronous completion and
culture changes still render. A derived `ShouldRender` must combine its decision with
`base.ShouldRender()`; custom markup reading additional state must widen the slice predicate,
or override it to return `true`. Toolbar/canvas child fragments and custom property templates remain conservative.

Stencil and template chooser retain the default: mutable stencil/template options and registry
members are read directly from Session, not completely represented by State. `BpmnEditorCanvas`
also tracks the session-only values its markup passes to the canvas. `BpmnPart` subscribes only
to the fixed session cascade; its DynamicComponent child receives State notifications directly
without requiring its parent to render. Component resolution caches include registry mutation
versions, not just options identity.

Property groups are cached by selected snapshots, document, read-only mode, provider identities
and UI culture. Changes to provider chains or editor-registry registrations also force a state-driven
render, even if the changed state slice is otherwise unrelated. Entry contexts, guarded callbacks and parameter dictionaries are reused while
their inputs remain unchanged. Revision keys remain intentional: they reset refused edits,
including expression editors and id renames. Validation rows/order/messages cache the report and
UI culture; widget positioning reuses styles for unchanged anchor/placement coordinates. Caches
retain only the current input set, not document/history generations.

`tests\Shared\RenderCountTests.cs` guards both kit compositions using renderer log events.
Run built Release kit test executables with `-class "*RenderCountTests" -showLiveOutput -noColor`.
Session microbenchmarks measure headless publication, not these component allocations; use
render budgets and cache identity/invalidation tests alongside the browser suites.
