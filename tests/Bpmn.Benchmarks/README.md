# Bpmn.Benchmarks

BenchmarkDotNet executable for managed editor costs. Targets .NET 10 and references the core
RCL and optional FeelSharp RCL; the latter does not add a dependency to the core package.
Run from the repository root, in Release:

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*"
# Measure only canonical XML writes:
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*WriteCanonical*"
```

`Program.cs` uses `BenchmarkSwitcher.FromAssembly`; except for the `memory`, `feel`, `export`, `fidelity`, `diff`, `inspection`, `snapshot`, `schema`, `descriptor-reuse`, `registry-cache`, `normalization`, `annotation-inspection`, `annotation-attachments`, `validation`, `accessible-graph` and `completion-corpus` report
commands, arguments after `--` are BenchmarkDotNet options. No demo server or Playwright browser
is needed. The RCLs'
normal build dependencies still apply when building the project.

## Accessible graph and lane embedding

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -p:ShouldRunBuildScript=false -- accessible-graph
npm --prefix src\Bpmn.Scripts run bench -- test\bench\lane-embedding.perf.ts
```

`AccessibleGraphBenchmarks` reports complete graph construction, fresh shallow
document construction and session semantic-edit reads at 8, 60, 600 and 5,000
flow nodes. Eight layouts cover flat/connected, ten lanes, one annotation per
node, deep/cyclic containers, fan-out and hidden endpoints. Every layout also
includes duplicate self-flows and an undrawn endpoint. Empty and no-diagram
results are separate untimed controls. Thirty warmups precede seven rounds,
with 100 calls per round below 100 nodes and three otherwise. Reported allocation
includes all operation-local data; document/fixture setup is outside measurement.
The SHA256 covers ordered graph fields, adjacency, attachment names/types and
accessible names. Reflection binds the internal attachment-type getter only
for benchmark equivalence reporting, outside timed work.

For comparison, preserve the untouched runtime and run the same report assembly
against it and the candidate. Keep inputs, runtime settings and harness identical.
Repeat with `DOTNET_TieredCompilation=0` when investigating tiering effects; do
not compare a default-runtime baseline against a tiering-disabled candidate.

`lane-embedding.perf.ts` measures complete embedding builds and fresh/incremental
JointJS model renders with tinybench. Five task counts (8, 60, 300, 1,000, 2,500)
cross no lanes, one root set, many sets and nested lanes of depth at most three.
Lane layouts have one lane per task and a pool: maximum **5,001 shapes**, not
2,500. No-lane controls have tasks and a pool. An ordered scan oracle verifies
every embedding map; rendered graph parents are also checked outside timing.
The printed rolling checksum is a comparison aid, not a cryptographic digest.
Mean/p99 figures use 100 ms measurement and 50 ms warmup budgets with minimum
iteration counts; short runs are exploratory, not confidence intervals.

Run benchmark pairs without concurrent builds, tests or browser workloads.
These reports measure managed/Node model work, not browser layout, SVG paint,
end-to-end editing latency or retained/peak memory. Paired results and rejected
experiments are recorded in [performance.md](../../docs/performance.md#lane-embedding-and-accessible-graph-allocation).

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

`EditorBenchmarks` does **not** measure JSON I/O, document-validation-only runs, rendered template/property panels,
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

## Whole-document validation

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- validation
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*ValidationBenchmarks*"
```

`ValidationBenchmarks` measures complete `BpmnValidator.Validate`, including its
operation-local indexes and all core validation layers. Fixtures cover 8/60/600/5,000
flow nodes in flat chains, many process containers, event subprocesses, gateway
fan-out, sparse/dense annotation bindings and invalid graphs with boundary escape
paths. Event-subprocess fixtures add one owning subprocess per three requested
nodes; output includes actual node count. The quick report also includes the
602-node stress fixture and 64/65-note controls around the annotation index gate.
Synthetic fixtures deliberately omit DI to isolate semantic validation; the stress
fixture includes DI. Document creation, XML reading and file I/O are outside timing.

The quick report uses 30 warmups and seven rounds, with 100 calls per round below
100 actual nodes and three otherwise. It reports median time and mean current-thread
allocation. `new-document` validates a fresh shallow record copy once per round,
including rebuilding the existing document element index; it is not process-cold
startup and does not include fixture construction. Output digests serialize every
ordered diagnostic, including localization keys and arguments. Compare the same
harness against untouched baseline/current libraries; a digest alone proves nothing.

Container groups, adjacency traversal and annotation association indexes remain
validation-local. Existing incoming/outgoing lookup groups are consumed without
copying per-node flow lists. The rebased validator retains local main's
annotation-only connection counts, avoiding association-list copies and counting
only declared annotation endpoints. Self-associations count once, duplicate
associations separately; null association endpoints do not enter the count map.
The 64/65-note cases remain small-workload controls, not a validator index threshold.
No document-global cache or new host mutation contract.
Timing remains exploratory, especially for sub-millisecond samples; compare default
tiering and separately labeled tiering-disabled runs, not pooled results.

## FeelSharp authoring

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*FeelSharpBenchmarks*" --job short --buildTimeout 600
# Quick allocation comparison, not a statistically controlled timing benchmark:
dotnet run -c Release --project tests\Bpmn.Benchmarks -- feel
```

`FeelSharpBenchmarks` varies expressions with 1/25/100 repeated terms and 10/1,000/10,000
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

### Completion traversal, 2026-11-05

Nested variable discovery now visits metadata directly rather than constructing
recursive iterator state machines and enumerators per entry. Candidate precedence,
bounded ranking and per-call observation of mutable metadata remain unchanged.

| Completion workload (25 terms) | Before | After |
|---|---:|---:|
| 10 variables, allocated/call | 9,664 B | 7,632 B |
| 1,000 variables, allocated/call | 432,752 B | 264,400 B |
| 10,000 variables, allocated/call | ~4,127 KB | ~2,447 KB |

Interleaved Release quick reports on one machine put the 1,000-variable case at
556-576 microseconds before and 333-358 microseconds after. These are exploratory
timings, not BenchmarkDotNet confidence intervals. Small-workload timings remain
noisy; allocation reductions are consistent.

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- completion-corpus
```

This deterministic 1,296-case output digest covers carets, selection ranges, nested
and duplicate metadata, ranking, custom functions, snippets and suppression. Run the
same harness against before/after libraries and compare its SHA256; a digest alone
without a baseline comparison is not an equivalence check. Setup also verifies that
the parser accepts valid syntax and rejects malformed syntax before reporting.

## Export loss analysis

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- export
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*ExportBenchmarks*"
```

`ExportBenchmarks` isolates loss analysis on equivalent retained/canonical XML trees
with 1/8/600/2,000/5,000 task siblings. This is not a complete export or a rendered
diagram benchmark. Setup checks that equivalent trees have no losses. The quick
report uses seven rounds of five calls (2,000 calls for the tiny sibling sets);
use BenchmarkDotNet for statistical analysis.

Wide parents now use per-pass namespace/name/ID lookup plus ordered same-name lists.
Small parents retain scanning. Duplicate IDs still match the first same-name ID;
ID-less children retain their original ordinal, including preceding identified
siblings. No persistent XML cache or source ownership change is introduced.

Local interleaved quick reports: 5,000-element loss analysis dropped from
185-236 ms to 3.5-4.2 ms; allocated bytes ranged from 2.21-2.53 MB before versus
approximately 2.14 MB after (decimal MB; tiering affects the baseline).
Tiny 1/8-sibling cases showed no repeatable timing regression and allocated less.
The real 602-shape stress fixture's single warmed canonical write sample
dropped from 70.8 ms/7.4 MB to 19.4 ms/7.1 MB. Treat that full-export timing as
directional; it is a one-call MemoryReport sample, not a confidence interval.

## Fidelity export

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- fidelity
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*FidelityBenchmarks*"
```

`FidelityBenchmarks` imports task chains of 1/8/9/600/2,000/5,000 nodes with no
redundant references (`ReferenceStride=0`), references on every node (`1`), or
sparse references (`1000`). Setup verifies import counts and unchanged XML.
`SynchroniseReferences` isolates already-correct reference synchronization;
`WriteFidelity` measures the complete retained-source save. Neither measures edits,
browser rendering or persistent memory.

The quick report warms each method five times, takes the median of seven timing
rounds, and prints allocated bytes/call and an output SHA256. Compare hashes against
the same harness with the baseline library; matching a digest without a baseline
does not establish equivalence. Tiny one-node cases repeat 1,000 synchronization
calls or 100 saves per round; other cases use five synchronization calls or one
save. These short samples are exploratory; use BenchmarkDotNet for confidence intervals.

Two interleaved Release comparisons on one machine, with 5,000 nodes:

| Workload | Before | After |
|---|---:|---:|
| All references, synchronization | 284-289 ms / 9.32 MB | 4.8-9.4 ms / 3.86 MB |
| All references, complete save | 333-336 ms / 39.52 MB | 60-92 ms / 29.97 MB |
| No references, complete save allocation | 26.26 MB | 15.60 MB |
| Sparse references, synchronization allocation | 2,428,728 B | 4,472 B |

MB values are decimal. Full sparse-save timing was noisy; no improvement is claimed.
All 18 output hashes matched across the four runs. Small 8/9-node synchronization
cases allocated less and showed no repeatable timing regression.

## Document diff

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- diff
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*DiffBenchmarks*"
```

`DiffBenchmarks` compares task chains of 8/600/5,000 nodes with 2/32 route points
per connector. Unchanged and edited variants are separate: edits rename every
tenth task and move every tenth connector's route. Setup validates result counts
and disables output truncation. Both documents and edits are prepared outside
measurement; the measured operation includes standard property inspection and
geometry across the document.

The quick report warms five times, takes seven timing rounds (50 calls per round
for eight nodes, one call otherwise), and prints allocations plus an ordered result
digest. Hashes cover this fixture's changed IDs, types, properties, endpoints and
geometry flags; setup asserts additions/removals are absent. They are not a general
document-equivalence corpus. Compare against the same harness with baseline
libraries, not a standalone hash.

Two interleaved local Release comparisons showed consistent allocation reductions:

| Unchanged document | Before | After |
|---|---:|---:|
| 8 nodes, 2-point routes | 78,958-79,005 B | 43,232 B |
| 600 nodes, 2-point routes | 6.02 MB | 3.32 MB |
| 600 nodes, 32-point routes | 9.88 MB | 3.89 MB |
| 5,000 nodes, 2-point routes | 50.58 MB | 28.02 MB |
| 5,000 nodes, 32-point routes | 82.74 MB | 32.82 MB |

MB values are decimal. Edited variants showed comparable allocation reductions.
All 12 output digests matched across four runs. Large-document timing was noisy,
with no reliable speedup; use BenchmarkDotNet before making timing claims.
The regression suite also covers all 529 flow-node type pairs, descriptor order,
null/absent values, multi-diagram geometry, live route-list mutation and an
allocation guard for 100 nodes with 32-point routes.

### Outgoing-flow scan follow-up

A fresh internal property reader now lazily indexes outgoing flows once per input
document/comparison, rather than scanning all sequence flows for every activity.
Single-element inspection remains unchanged; documents of up to eight flow nodes
keep scanning. Reader state is operation-local, not a document cache.

Using the same `diff` harness, two interleaved comparisons against the allocation
fix above measured unchanged 5,000-node cases at 377-389 ms before versus 46-64 ms
after (2-point routes), and 429-463 ms versus 48-62 ms (32-point routes).
All 12 output digests matched in four runs. Timings are exploratory quick-report
medians, not BenchmarkDotNet confidence intervals. Small cases added 192 B for two
reader closures without a repeatable timing regression; large-case allocations
remained roughly 27-33 MB. Descriptor tests cover both sides of the indexing
threshold and zero/one/many outgoing flows; diff tests cover mutable host flow lists.

## Extension inspection

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- inspection
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*InspectionBenchmarks*"
```

`InspectionBenchmarks` builds one task containing a mapping with 1/8/9/600/5,000
extension children, each with two attributes and leaf text. Names are either all
unique or repeat across three local names; namespaces alternate independently.
`ExtensionProperties` isolates raw descriptor construction. `Describe` includes
the complete localized snapshot, validation, references and template processing;
neither method measures UI rendering or interaction.

The quick report uses five warmups and seven timing rounds. Small raw cases repeat
100 calls per round and full snapshots 20; wide cases use one call per round.
Setup asserts descriptor counts. Ordered output hashes cover raw paths, labels,
kinds, values and read-only flags; compare with the baseline library using the
same harness. They do not prove complete snapshot equivalence.

Two interleaved local Release comparisons, 5,000 children:

| Workload | Before | After |
|---|---:|---:|
| Raw descriptors, unique names | 148-163 ms | 7.3-9.7 ms |
| Raw descriptors, repeated names | ~171 ms | 8.1-8.2 ms |
| Full snapshot, unique names | 160-176 ms | 18-22 ms |
| Full snapshot, repeated names | 201-227 ms | 24-35 ms |

These are exploratory quick-report medians, not BenchmarkDotNet confidence intervals.
All ten raw descriptor hashes matched across four runs. Per-traversal name counts
replace repeated sibling scans only above eight entries. Unique-name indexing
increases raw allocation from 5.80 MB to 6.25 MB at 5,000 children (decimal MB);
repeated-name allocation remains approximately 6.26 MB. Eight-entry and smaller
cases retain their original allocations. Nine unique names add 1,032 B of index
storage, with no repeatable timing regression in these runs.

## Snapshot capability reuse

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- snapshot
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*SnapshotBenchmarks*"
```

`SnapshotBenchmarks` describes a task with 0/8/600/5,000 outgoing flows to one end
event. Complete snapshots include invariant checks, references, localization and
default-flow choices. Fixture creation is outside measurement. Setup checks label
capability and choice count. The quick report warms five times, takes seven rounds
of 100 calls for 0/8 flows or five calls otherwise, and prints allocations plus a
digest of capabilities, standard descriptors and choices.

Two interleaved local Release comparisons:

| Outgoing flows | Before allocation | After allocation |
|---|---:|---:|
| 0 | 9,056 B | 8,328 B |
| 8 | 14,144 B | 12,472 B |
| 600 | 283,032 B | 236,672 B |
| 5,000 | 2,255,672 B | 1,883,352 B |

Capability detection now consumes the standard descriptors already built for the
snapshot instead of rebuilding them. All four output digests matched across four
runs. Timings overlapped; no speed claim is made. This is snapshot allocation,
not retained memory or browser latency. Registered-schema raw filtering remains
unchanged; custom builder hooks have a separate mutation/ownership contract.

### Unchanged localized choices

A subsequent comparison using the same `snapshot` harness avoids cloning immutable
choice records when localization leaves their label unchanged. Descriptor, property
entry and schema-group paths share this rule. Arrays are still copied; mutable
source choice lists do not become shared output lists. No translations are cached.

| Outgoing flows | Before allocation | After allocation |
|---|---:|---:|
| 0 | 8,328 B | 8,168 B |
| 8 | 12,472 B | 11,952 B |
| 600 | 236,672 B | 212,472 B |
| 5,000 | 1,883,352 B | 1,683,152 B |

Two interleaved local Release comparisons produced matching four-workload output
digests across all four runs. Timing showed no reliable speed gain; the eight-flow
case measured 0.023-0.025 ms before versus 0.028 ms after. This is an allocation
improvement, not a latency claim. Tests exercise changing localizer results,
fallback restoration, source-list replacement and all three localization paths.

## Schema inspection and connector normalization

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- schema
dotnet run -c Release --project tests\Bpmn.Benchmarks -- normalization
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*SchemaInspectionBenchmarks*" "*NormalizationBenchmarks*"
```

Use SDK 10 explicitly if another installed SDK produces Razor build failures.
Local comparisons used SDK 10.0.403/.NET 10.0.14, Windows x64, with
`-p:ShouldRunBuildScript=false` and existing script bundles.

`SchemaInspectionBenchmarks` measures the built-in builder separately from full
localized `Describe` snapshots. Parameters cover no schemas, Zeebe, Camunda 7,
both, and both plus four packages with 25 unrelated types each; service tasks,
call activities and end events; empty/600-row collections; and
`AlwaysOfferRegisteredSchemas` true/false. The quick report selects 21 combinations.
Direct builder measurements always use the supplied full registry; namespace
filtering applies only to `Describe`, as in production.

`NormalizationBenchmarks` measures `ConnectorEndpoints.Normalize`, complete XML
read, and public single-shape move commands separately. Parameters cover
2/600/5,000 nodes, 2/4/32 points, valid/off-outline endpoints and flow-attached
notes. The quick report selects 18 annotation-free cases plus two 600-node cases
with 60 notes. Setup verifies edge count, idempotence and that displaced endpoints
actually require correction. Each move starts from the same source document;
there is no accumulating history.

Both reports warm each operation five times, then take seven timing rounds.
Schema calls/round: 50 for empty collections, two for wide collections.
Normalization calls/round: 50 for two nodes, one otherwise. Fixture construction,
XML generation and hashing are outside measurement. SHA256 outputs cover complete
builder groups/claimed roots and snapshots, or normalized/imported/moved model JSON
plus normalization/import/edit diagnostics. Compare identical harnesses against
both library versions; hashes alone do not prove all possible inputs equivalent.

Local interleaved comparisons reduced Zeebe-only service-task snapshot allocation
from 270,112 B to 76,704 B. Unchanged 5,000-node/32-point normalization fell from
3,902,448 B to 203,904 B; complete move allocation fell from 9.69 MB to 5.43 MB.
These are managed allocation results, not retained-memory or browser latency claims.

Tiered quick timings included a 600-node/32-point regression, so geometry was
also compared with tiering disabled:

```powershell
$env:DOTNET_TieredCompilation = "0"
dotnet run -c Release --project tests\Bpmn.Benchmarks -- normalization
Remove-Item Env:\DOTNET_TieredCompilation
```

Apply the same runtime setting to both versions. Optimized-code 600-node/32-point
normalization measured 0.46-0.65 ms before versus 0.31-0.32 ms after, but full
XML-read timing overlapped. Do not mix the two runtime configurations or infer
startup performance from these samples. All 21 schema and 20 geometry digests
matched across four default-runtime runs; geometry also matched four
tiering-disabled runs. See [measurements and caveats](../../docs/performance.md#operation-scoped-schema-applicability).

## Browser comparison

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

## Annotation inspection and attachments

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- annotation-inspection
dotnet run -c Release --project tests\Bpmn.Benchmarks -- annotation-attachments
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*AnnotationInspectionBenchmarks*"
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*AnnotationAttachmentBenchmarks*"
```

`AnnotationInspectionBenchmarks` separates annotation-policy inspection, reading,
validation and complete import preview. Its 13-case quick report covers
2/600/5,000 nodes, XML with retained source versus JSON, absent/present notes and
multiple connections. Setup verifies note counts, acceptance and consent/removal
requirements. Annotations still require the existing consent path; the empty-note
fast path avoids copying retained XML without exposing it.

`AnnotationAttachmentBenchmarks` separates shape following, flow following, public
edit application and XML reading. Its 15-case quick report covers absent/single/
60/500 notes, shape targets, distinct flows and one shared rounded flow. Six-point
routes include short jogs. `Changed=false` applies only to the isolated following
operations: the public `Edit` method always applies the prepared move or reroute.
Each invocation starts from the same document, not the previous result. Setup
checks successful edits and actual note movement in changed cases.

Both reports warm each method five times and take seven timing rounds, reporting
median elapsed time and mean allocated bytes/call. Tiny cases use 20 calls per
round; larger cases use one. Fixture creation, serialization and hashing are
outside measurement. These are exploratory quick samples, not confidence
intervals; use BenchmarkDotNet for statistical timing analysis. A separate
`DOTNET_TieredCompilation=0` comparison checks sensitivity to tiering transitions,
not startup performance.

Compare all 13 inspection or 15 attachment hashes against the same harness with
baseline libraries. Inspection hashes cover normalization/consent, preview
diagnostics, validation/status, model JSON and fidelity XML. Attachment hashes
cover both following results, moved-connector reports, public edits and diagnostics,
and imported model/diagnostics/fidelity XML. They establish equivalence for these
fixtures, not every possible host document.

Large attachment sets use operation-local first-match plane and ordered
association indexes above 64 annotations and associations. Smaller sets retain
scans because 60-note indexing did not consistently repay its allocation cost.
Flattened flow paths are reused above eight annotations and associations, keyed
by edge reference identity; distinct routes are never merged by BPMN ID.
Shared-flow edits allocate substantially less; distinct-flow indexing can trade
additional allocation for faster large edits. See
[measured results and tradeoffs](../../docs/performance.md#annotation-inspection-and-attachment-following).

## Descriptor reuse and registry caches

```powershell
dotnet run -c Release --project tests\Bpmn.Benchmarks -- descriptor-reuse
dotnet run -c Release --project tests\Bpmn.Benchmarks -- registry-cache
dotnet run -c Release --project tests\Bpmn.Benchmarks -- --filter "*RegistryCacheBenchmarks*"
```

`descriptor-reuse` uses `SchemaInspectionBenchmarks` for 16 complete-snapshot
samples: no/Zeebe/Camunda/both schemas, 0/1/600 input rows, built-in versus
forwarding custom builders/labels and editable/read-only output. The both-schema
editable built-in case is deliberately repeated before the custom callback
control. Only sealed built-in builders plus default labels allow reuse of the
initial raw descriptors. Custom callbacks keep the fresh post-builder traversal;
the template provider still receives its original pre-builder descriptor list.

`RegistryCacheBenchmarks` registers Zeebe and Camunda plus a synthetic package
with 3/32/128 ordered children and 1/8 inheritance levels. The six cases measure
warmed properties, child order, insertion index and complete extension-add edits.
Setup checks inheritance counts, insertion position and edit application.
Every edit starts from the same immutable document; no cumulative edit/history
or browser rendering is measured.

Registration is reported separately. `ColdChildren` includes creating/registering
all three packages and the first child-order query; `AfterRegistration` includes
that setup, warming both queries, adding a late trait and querying again.
Neither is an isolated registration or cache-miss timer. Identical registration
is also measured. `retained-query-growth-per-registry` holds 16 fresh registries
alive across forced collections and warms both queries for **every** registered
type between samples, reporting mean retained growth. This approximate managed
heap measurement excludes native memory, peak transient allocations and realistic
query frequency; normal lazy caches may populate fewer entries.

Quick reports warm each method five times and take seven rounds. Descriptor
samples use 50 calls per round for empty rows, two otherwise. Registry samples
use 50 query calls, five insertion/registration calls or two complete edits.
Results are exploratory median timings and mean allocated bytes/call, not
BenchmarkDotNet confidence intervals. Fixture setup and output serialization/
hashing/retention probes are outside measured methods.

Compare all 16 descriptor hashes and six registry hashes against the same harness
with baseline libraries. Descriptor hashes serialize complete snapshots; registry
hashes include ordered property metadata, child order, insertion position,
post-registration properties, edit diagnostics, model JSON and fidelity XML.
These fixtures do not cover every host callback: regression tests separately
exercise mutable extras/snapshot lists, template fallback, late bases/traits/
aliases/cycles, external type records and concurrent registration.

The caches belong to one registry instance, retain only registry-owned type
identities and clear under the existing lock after each successful new
registration. Returned lists remain read-only; external type records stay
uncached. Query hits allocate nothing, but cold fills and retained cache storage
are not free. See [measurements and costs](../../docs/performance.md#descriptor-ownership-and-registry-query-caches).
