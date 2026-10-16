# Bpmn.Benchmarks

BenchmarkDotNet executable for managed editor costs. Targets .NET 10 and references the core RCL.
Run from the repository root, in Release:

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*"
# Measure only canonical XML writes:
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*WriteCanonical*"
```

`Program.cs` uses `BenchmarkSwitcher.FromAssembly`, so arguments after `--` are BenchmarkDotNet
options, not application arguments. No demo server or Playwright browser is needed. The core RCL's
normal build dependencies still apply when building the project.

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

This suite does **not** measure JSON I/O, document-validation-only runs, template/property panels,
JS interop, JointJS routing, browser paint, pan/zoom frames or virtualization. See
[Performance budget](../../docs/performance.md) for canvas measurements and browser harnesses.
Compare before/after runs using the same fixture, hardware and build configuration; benchmark
results are local measurements, not universal performance guarantees.

Implementation: [`EditorBenchmarks.cs`](EditorBenchmarks.cs), [`Program.cs`](Program.cs), and
[project fixture copy](Hasseware.AspNetCore.Components.Bpmn.Benchmarks.csproj).


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

Run the Release test executables directly (the current runner does not discover tests through
`dotnet test`), using `-class "*RenderCountTests" -showLiveOutput -noColor` to see counts.
Baseline tables and machine-specific timings belong in the session's `files\perf-baseline.md`
artifact, not portable performance guarantees.
