# Bpmn.Benchmarks

BenchmarkDotNet executable for managed editor costs. Targets .NET 10 and references the core
RCL and optional FeelSharp RCL; the latter does not add a dependency to the core package.
Run from the repository root, in Release:

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*"
# Measure only canonical XML writes:
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*WriteCanonical*"
```

`Program.cs` uses `BenchmarkSwitcher.FromAssembly`; except for the `memory` and `feel` report
commands, arguments after `--` are BenchmarkDotNet options. No demo server or Playwright browser
is needed. The RCLs'
normal build dependencies still apply when building the project.

## What stays in memory

BenchmarkDotNet reports what a call allocates, not what a document, a history or a source tree keeps alive. `MemoryReport` does, after forced collections:

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- memory [path-to.bpmn]
```

It prints allocation and time for the XML read and canonical write, the size of the `loadDocument` interop payload and a first-node deletion patch, and retained megabytes for a document with and without its
source XML tree and for a 100-step undo history. It also compares canonical XML returned as a string with canonical XML written directly to a stream. The figures recorded on the stress fixture are in [docs/performance.md](../../docs/performance.md).

## What EditorBenchmarks measures

`GlobalSetup` loads `large-virtualization-stress.bpmn` from the benchmark output directory and
imports it once. The project copies the fixture from the demo's `wwwroot\samples` at build time;
there is no second maintained fixture. Setup selects the first diagram and prepares moves that
shift its first twenty shapes by **10 model units on X**.

| Method | Timed operation |
|---|---|
| `Read` | Parse the stored XML string with `BpmnXmlReader.Read` |
| `WriteCanonical` | Generate canonical XML with `BpmnXmlWriter.Write` |
| `AccessibleGraph` | Build `BpmnAccessibleGraph.For` for the selected diagram |
| `MoveShapes` | Apply a twenty-shape `MoveShapesCommand` using `BpmnDocumentEditor.Apply` |

`[MemoryDiagnoser]` records managed allocations as well as timing. Fixture file I/O and initial
setup are outside the measured methods. Moves start from the same immutable source document on
each invocation; this is command cost, not a cumulative drag or an undo-history benchmark.

## Scope and interpretation

This suite does **not** measure JSON I/O, template/property panels,
JS interop, JointJS routing, browser paint, pan/zoom frames or virtualization. See
[Performance budget](../../docs/performance.md) for canvas measurements and browser harnesses.
Compare before/after runs using the same fixture, hardware and build configuration; benchmark
results are local measurements, not universal performance guarantees.

Implementation: [`EditorBenchmarks.cs`](EditorBenchmarks.cs), [`Program.cs`](Program.cs), and
[project fixture copy](Hasseware.AspNetCore.Components.Bpmn.Benchmarks.csproj).

## Managed rendering passes at scale

`ManagedRenderingBenchmarks` builds normalized synthetic connected diagrams at 602, 2,000 and 5,000 shapes, with a named task
per shape and one sequence flow between neighbours. It measures unchanged endpoint normalization, following translated shapes,
and accessing a warmed session graph while alternating geometry-only document versions. Reflection binds internal geometry
delegates during setup, outside the measurements. Both attachment-normalization passes remain enabled.

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*ManagedRenderingBenchmarks*" --job Dry --inProcess --iterationCount 5 --warmupCount 2
```

This short in-process run is useful for allocations, not stable timing conclusions. `-p:ShouldRunBuildScript=false` can skip
rebuilding already-current script assets when repeatedly running managed benchmarks. Do not use it to bypass a required asset build.
For paired JointJS full/incremental model rendering at the same sizes, run `npm --prefix src\Bpmn.Scripts run bench`.
For actual SVG, edit, frame and heap observations, use `node src\Bpmn.Scripts\tools\perf-bench.mjs <demo-url> --runs=3`.
The default XML sample has 602 shapes; pass both `--fixture=<sample-name>` and `--shapes=<accessible-item-count>` for another sample.

## Semantic rendering and validation at scale

`SemanticRenderingBenchmarks` separates accessible-graph construction from core validation on three deterministic layouts.
`TaskCount` is 602, 2,000 or 5,000 tasks, **not total shapes**:

| Layout | Additional elements |
|---|---|
| Flat | One sequence flow between consecutive tasks |
| Nested | One expanded subprocess per ten tasks; flows stay inside each subprocess |
| Annotations | One text annotation and association per task, plus the flat sequence flows |

The annotation layout therefore has twice as many shapes as tasks. These cases expose container and association scans that
the flat stress fixture does not. Setup is outside measurement; repeated calls use the same immutable document and its warmed
element index. Core validation includes all core rules but not host profiles or session publication.

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*SemanticRenderingBenchmarks*" --job short --inProcess --warmupCount 3 --iterationCount 5 --iterationTime 100
```

Use the same job for before/after comparisons and avoid concurrent browser/build workloads. Dry jobs are useful for allocation
exploration but their one-call timing samples include substantial JIT and scheduler noise.
For ordered diagnostic equivalence checks across builds, `validation-reports <fixture-directory>` emits JSON containing
every core diagnostic and its presentation metadata, with fixture names sorted ordinally. It does not sort diagnostics or
run host profiles.

The browser harness accepts `--profile=<file-prefix>` to save a Chromium CPU profile for each rename commit as
`<file-prefix>.rename.<run>.cpuprofile`. Profiles are diagnostic runs, not interchangeable with unprofiled latency samples.
Release WebAssembly samples may lack managed method names; a native/interpreter frame alone does not identify validation or
Blazor tree diffing. The model benchmark also reports document-index, plane-index and embedding preparation separately;
those isolated timings exclude reconciliation, SVG mounting and paint.
Use `--isolate-pan` for a diagnostic run that clears selection on a verified blank canvas point and waits for it to settle
before sampling the pan. Default runs retain the original selection-inclusive gesture for comparability.

## Swimlane reading-order scaling

`SwimlaneGraphBenchmarks` measures accessible-graph construction with 602, 2,000 or 5,000 tasks and 1, 10 or 100 lanes
inside one pool. Consecutive tasks have sequence flows; lane memberships are disjoint and interleaved in task document order.
Total shapes are `TaskCount + LaneCount + 1`. Setup is not timed. These are managed graph-building costs, not validation,
SVG projection, browser edit latency or pan/zoom measurements.

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*SwimlaneGraphBenchmarks*" --job short --inProcess --warmupCount 4 --iterationCount 7 --iterationTime 250
```

Compare all nine cases, including small-lane controls. Avoid concurrent builds/browser workloads; repeat the optimized
run after the baseline to expose order effects. Local Round 5 results and allocation trade-offs are in
[docs/performance.md](../../docs/performance.md). `AccessibleGraphTests` compares complete graph content with the original
full-scan interleaving algorithm on every plane in the shipped samples, plus overlapping/duplicate and undrawn memberships.

## FeelSharp authoring

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*FeelSharpBenchmarks*" --job short --buildTimeout 600
# Quick allocation comparison, not a statistically controlled timing benchmark:
dotnet run -c Release --project tests\Bpmn.Benchmarks -- feel
```

`FeelSharpBenchmarks` varies expressions with 1/25/100 repeated terms and 10/1,000
multiword variables, each with a nested member. Setup creates source, tokens and metadata
outside measurement. Tokenization, completion, signature help, local inference, syntax
validation and explicit evaluation are separate methods. Completion enables locals,
ranking and snippets and consumes cached tokens. Evaluation includes a new restricted
engine and evaluates `sum([1, 2, 3])`; it does not vary with the authoring parameters.

`ColdParse` means a **new parser instance in a warmed process**, not process-cold startup
or guaranteed uncached upstream parsing. `Parse` repeatedly validates identical text
on the same parser. The quick report warms each method ten times, then reports the median
of seven repeated-call timing rounds and mean allocated bytes/call. Tiering, upstream
caches and short samples make its timings exploratory. Use BenchmarkDotNet for timing
conclusions and the long-expression corpus in `EnhancementTests` for additional input shapes.

Local Release before/after allocation samples (25 terms, same options and metadata):

| Operation | Before | After |
|---|---:|---:|
| Completion, 10 variables | 23,960 B | 9,664 B |
| Completion, 1,000 variables | 874,440 B | 432,752 B |
| Signature help | 1,688 B | 336 B |
| Explicit evaluation | 51,000 B | 51,000 B |

These are allocation-shape observations, not cross-machine budgets or speed guarantees.
The regression suite bounds the large-variable completion allocation and checks mutable
metadata, ranking, replacement ranges, UTF-16 boundaries and custom signature precedence.
An additional local 4,000-case differential probe matched completion output exactly.

### Browser comparison

Publish `playground\Bpmn.Demo\Bpmn.Demo.csproj` in Release, run the published host in
Production, and exercise `/feel-authoring-server` and `/feel-authoring` with
`?plain=true` and `?plain=false`. Separate fresh browser contexts from warm-cache pages.
Visit Reference once, return to Editor, then submit twenty inputs of
`'1 + '.repeat(i) + 'am'`, each followed by Left, Right and click. Count
`DotNet.DotNetObject.prototype.invokeMethodAsync` by method, serialized argument
characters, relevant `getBoundingClientRect` calls and `getComputedStyle` calls.
Measure input until the highlight text matches; this is not paint or diagnostic completion.

Local Chromium runs, three pages per route/kit:

| Work per twenty-input sequence | Before | After |
|---|---:|---:|
| `SnapshotAsync`, either kit | 80 | 61 |
| Native delegated `DispatchEventAsync` | 60 | 0 |
| Native serialized argument characters, all observed calls | 21,997 | 4,291 |
| Native geometry / computed-style reads | 634 / 159 | 558 / 140 |
| Fluent geometry reads | 960 | 672–762 |
| Fluent computed-style reads | 322 | 197–242 |

Fluent retained 61 delegated events: its enhanced input intentionally suppresses
rerenders to protect native draft ownership. Workspace typing no longer rerenders
visited reference content. Highlight latency improved locally, but short browser runs,
runtime warmup and instrumentation do not establish portable timing guarantees.
DOM mutation counts and compressed startup transfer remained effectively unchanged.
Supplemental checks verified Brotli FEEL assemblies, responsive popovers, native undo,
manager completion/Escape and FEEL-owned observer counts returning to baseline after
three manager closes. Observer counts do not measure retained heap or third-party
library garbage collection. Modal-ready observations ranged 45–183 ms locally; no
before/after modal or separately instrumented initial-diagnostics timing is claimed.

## Session publication and composition render baseline

`SessionBenchmarks` uses the same 602-shape fixture. Setup constructs a headless session,
assigns the document and first diagram, calls `UpdateParameters` as `BpmnEditorScope` does,
and selects the first flow node. Validation-on-change and accessible-tree state remain enabled.

| Method | Timed operation |
|---|---|
| `ViewportChanged` | Alternate zoom 1.0/1.1 through the canvas's `HandleViewportChangedAsync` callback path |
| `SelectionChanged` | Alternate two preallocated single-element selections through `SetSelectionAsync`, without a canvas |
| `UnchangedParameters` | Publish the same configuration and document references through `UpdateParameters` |
| `SingleEdit` | Change the first flow node's name through `ApplyAsync`, including validation and state publication |
| `RefreshState` | Rebuild state alone, through a cached private-method delegate; reflection binding is outside measurement |

Only `SingleEdit` resets its session before each iteration, keeping history empty and the source
fixture unchanged. This forces one invocation per iteration; short-job edit timings are exploratory,
not statistically strong. Other methods measure warmed sessions. No canvas or JS interop is attached.

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*SessionBenchmarks*" "*AccessibleGraph*" --job short --buildTimeout 600
```

The extended build timeout accommodates the core RCL's TypeScript/static-asset build in
BenchmarkDotNet's isolated output directory. Stop any demo host holding build output locks first.

`tests\Shared\RenderCountTests.cs`, linked into both kit test projects, measures full
`BpmnEditor`/`FluentBpmnEditor` compositions with the same fixture, stencil, property panel,
accessible tree, and a footer containing validation and navigation parts. Each scenario starts
from a fresh composition with one element selected. It prints per-type render deltas and session
notifications for viewport, selection, unchanged parent parameters, and one name edit.

Counters use Blazor's structured `RenderingComponent` debug log events, not bUnit's wrapper
`RenderCount`: the latter also increments ancestors when descendants update. Counters track
component IDs across replacement/disposal, exclude Fluent primitives and `DynamicComponent`
plumbing, and wait for two quiet dispatcher samples before each measurement. JS interop is mocked;
selection deliberately has no simulated JS echo callback. Upper-bound assertions guard
state-publication budgets in both kits: viewport 6, selection 11,
one edit 21, and unchanged parent parameters 19 tracked renders. Per-part assertions also prevent
viewport publications from rendering property/validation panels and closed widgets. These are
managed composition counts, not browser frame or paint budgets.
A name edit also asserts one acknowledged patch without a second state notification for unchanged loaded counts.

Run the Release test executables directly (the current runner does not discover tests through
`dotnet test`), using `-class "*RenderCountTests" -showLiveOutput -noColor` to see counts.
Baseline tables and machine-specific timings belong in the session's `files\perf-baseline.md`
artifact, not portable performance guarantees.

## Accessible presentation attribution

`AccessiblePresentationBenchmarks` uses synthetic connected diagrams with 602, 2,000 or 5,000 named tasks.
It separates complete-tree text from graph derivation and validation; setup, DI construction and reflection binding are
outside measurement.

| Method | Timed operation |
|---|---|
| `RenderNames` | Enumerate all descriptions with one render-local localizer scope |
| `LocalizedNames` | Enumerate descriptions through independent public localizer calls |
| `TypeNames` | Resolve each node's element-type caption |
| `Graph` | Build the complete accessible graph |
| `CoreValidation` | Run all core validation rules |
| `ProfileValidation` | Run core rules plus the template profile with default empty options |

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*AccessiblePresentationBenchmarks*" --job short --inProcess --warmupCount 4 --iterationCount 7 --iterationTime 250
```

For matched comparisons against a preserved pre-scope core assembly, `RenderNames` binds the internal scope factory once
during setup, falling back to the original public localizer when that method is absent. Use the same benchmark executable
and compatible dependency set for both assemblies, restore the current assembly afterwards, and run sequentially without
builds, tests or browser workloads. This fallback is benchmark compatibility, not application error handling.

These figures exclude Blazor diffing, JS interop, SVG paint and host-specific profile/template workloads. In-process
mixed-call runs can change JIT dispatch behavior: retain `LocalizedNames` as an unscoped control and do not infer its
performance from the render-scoped result. Round 6 results and observed control sensitivity are recorded in
[docs/performance.md](../../docs/performance.md).

`AccessibleTreeTests.SelectionRenderAttributionCountsLocalizedWork` covers ten actual 602-item selection renders with a
counting localizer. Its lookup upper bound is deterministic; its logged elapsed time is diagnostic, not a timing assertion.
Culture, fallback and mutable-host tests ensure the optimization does not retain translations across renders.

## Patch transport and applied-template workloads

`CanvasPatchBenchmarks` separates patch construction, outgoing patch serialization and uncached full projection.
It uses 602/2,000/5,000 connected tasks and six changes: rename, move, non-canvas semantic edit, delete, undo delete,
and reconstructed equivalent flow-node collections. Commands and fixture creation run during setup, not measurement.
The full-projection target deliberately bypasses the document cache; it is a fallback-cost control, not the normal edit path.
`SessionBenchmarks.SingleEdit` remains the command/validation/publication target.

`TemplateValidationBenchmarks` separates core-only, profile-only and combined validation at the same task counts.
`None` has no applied markers. `Plain` applies one shared Camunda 7 template with three top-level properties to every task:
a stored name binding, missing required FEEL and a conditional required value. `Scoped` adds a missing required connector
input. Both applied cases register version 2 to exercise upgrade selection. These intentionally produce findings, rather
than timing a hidden-property-only happy path. `InspectTargets` measures the full property-panel inspection loop that the
original profile used; it remains a diagnostic control, not the optimized profile path.

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*CanvasPatchBenchmarks*" "*TemplateValidationBenchmarks*" --job short --inProcess --warmupCount 3 --iterationCount 5 --iterationTime 100
```

Run sequentially without browser/build workloads. Single-invocation Dry runs help detect large costs but include JIT noise;
do not use them as final timing comparisons. For compatible preserved core assemblies, reuse the same benchmark executable
and dependencies, swap only the core assembly, then restore the current assembly in a `finally` block.
Neither template synthetic timings nor patch-only costs imply a default browser-edit speedup.

For browser comparisons using an existing picker entry but local XML with applied-template markers, the harness accepts:

```powershell
node src\Bpmn.Scripts\tools\perf-bench.mjs http://localhost:5410 --runs=5 --fixture-file=C:\absolute\path\template-stress.bpmn
```

`--fixture-file` replaces only the selected `--fixture` sample response through Playwright routing. The picker entry must
exist, `--shapes` must still match, and templates must already be registered by the host. Routing disables HTTP caching:
the reported reload time is not a cache-warm-start measurement in this mode. Compare equivalent overridden workloads,
not their load numbers against un-routed runs. Keep CPU profiles (`--profile=<prefix>`) separate from latency series.

The Round 7 template browser case starts from `large-virtualization-stress.bpmn`, changes its 600 `userTask` elements
to `serviceTask`, and adds `modelerTemplate="demo.camunda7.mail"` and `modelerTemplateVersion="1"` in
`http://camunda.org/schema/1.0/bpmn` to each task. Preserve IDs, names, DI and connections. The demo already registers
that template; leave the required recipient absent to exercise validation findings. Save the transformed XML outside the
repository and pass its absolute path through `--fixture-file`; compare it with the same transformed XML on both builds.

## Cold and warm selection inspection

`SelectionInspectionBenchmarks` uses 602/2,000/5,000 connected tasks and selections of 1/20/100 tasks.
The plain case has no extensions; the template case applies one shared Camunda 7 template with a scoped required connector
input. `ColdSnapshots` clears only the existing session description dictionary and reads the complete selection snapshots.
`WarmSnapshots` reads an independently prewarmed session. `Invariants` measures one complete model-invariant pass as a control.
Fixture/session setup, graph building, selection publication and core/profile validation are outside these timed methods.
Reflection resolves the cache once during setup, not per invocation. Clearing the dictionary is included and identical in
both builds; this is not end-to-end selection latency or a cold application launch.

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*SelectionInspectionBenchmarks*" --job short --inProcess --warmupCount 3 --iterationCount 5 --iterationTime 100
node src\Bpmn.Scripts\tools\perf-bench.mjs http://localhost:5410 --selection --runs=5
```

`--selection` enables Properties, selects all shapes through the real canvas Ctrl+A shortcut and waits for the exact
property-panel selection count plus canvas settling. Escape clears selection before the warm repeat and before returning
to the default interaction sequence. It repeats after delete/undo and rename/undo invalidate descriptions.
The default 602-shape sample selects 602 semantic shapes, not sequence flows; `--shapes` must match that count.
The accessible tree stays enabled. Selection timing includes panel composition, state publication, JS selection outlines,
DOM work and the harness's settling interval; compare it only against equivalent `--selection` runs.
The temporary panel toggles and cache warming also change later interactions relative to a run without this flag.
Selection CPU profiles are saved separately as `<prefix>.selection-load.<run>.cpuprofile` and
`<prefix>.selection-after-edit.<run>.cpuprofile` when `--profile` is supplied. Do not mix profiled and unprofiled latency.

For warm selection-size and panel-visibility controls:

```powershell
node src\Bpmn.Scripts\tools\perf-bench.mjs http://localhost:5410 --selection --selection-controls --runs=5
```

`--selection-controls` requires `--selection` and runs after the original selection/delete/rename sequence, before pan.
It measures one task with Properties open, that task with Properties hidden, then all shapes with Properties hidden.
Each action waits for the real host selection status and canvas settling, and Escape clears selection between actions.
The accessible tree and toolbar stay enabled. The task is clicked away from its center so a sequence-flow wrapper does not
intercept the pointer. These controls assume the demo's English status text and a sample with a mounted activity.
Run both versions with identical flags; panel toggles and single-selection state can affect subsequent pan/zoom measurements.
Compare unprofiled series and reverse build order or interleave runs to expose order effects.
Rounds 9 and 10 results are recorded in [performance.md](../../docs/performance.md). Round 10's lazy geometry
dictionaries live only within one multi-selection snapshot read. They preserve first-match DI and active-plane precedence,
rebuild after document replacement/diagram changes, and are not built for warm or singleton inspection. The native fixture
does not select an active diagram, so it measures the global lookup portion, not the additional browser active-plane work.
Include allocation results: temporary dictionaries reduce visits but add allocations, and short templated/small-selection
controls are not uniformly faster. Retention requires repeated full-browser cold-selection gains, not native timing alone.
If warm selection, single selection and rename controls all drift together, preserve that series as non-comparable evidence
and repeat the matched comparison; do not normalize it into the reported speedup.

`AccessibleTreeTests.SelectionRenderAttributionCountsLocalizedWork` now records ten complete selection renders at
602/2,000/5,000 items, including total process allocations. That diagnostic includes bUnit/DOM simulation and dispatcher
work; it is not a browser allocation measurement or a timing assertion.
