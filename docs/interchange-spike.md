# Interchange spike: can `Bpmn.Interchange` be this editor's XML backend?

**Date:** 2026-09-19
**Decision: no.** The editor owns its BPMN XML reader and writer. The `Bpmn.Interchange` and
`Bpmn.Model` package references have been removed from
`src/Bpmn/Hasseware.AspNetCore.Components.Bpmn.csproj`.

## Reading this historical decision today

This report records the package experiment and its fixture counts at the date above, not the
current editor's support matrix. The shipping serializers live in
[`src/Bpmn/Serialization`](../src/Bpmn/Serialization/); current guarantees and loss handling are
in [XML interchange](xml-interchange.md). [JSON interchange](json-interchange.md) documents the
later versioned model format and why it cannot replace XML fidelity saves. The current
[validation guide](validation.md) separates readable files from exportable documents.

## Why this was measured

The implementation plan originally recorded, as confirmed, that `Bpmn.Model`'s object model
*is* the editor's semantic model of record and that XML I/O would be delegated to
`Bpmn.Interchange`. Reading the package's public surface cast doubt on that, so the claim was
measured rather than argued: all 12 fixtures in `playground/Bpmn.Demo/wwwroot/samples/` were read
with `BpmnXmlReader.Read` and written back with `BpmnXmlWriter.Write`, and the result compared
with the source.

## What the measurement showed

### It does some things well

- **DI geometry survives exactly.** Every fixture's first `BPMNShape` came back with identical
  `x`, `y`, `width`, `height`, including non-integral values (`1143x306.5`). Shape and edge
  counts were preserved in all 12 files.
- **Vendor extensions survive.** The Zeebe fixtures round-tripped without loss.
- **Its diagnostics model is good.** Element-scoped `Info`/`Degraded`/`Dropped` findings from the
  same code path as the real read. This library's `BpmnImportDiagnostic` follows that design.

### But it drops content an editor must keep

Three classes of loss, all unacceptable for a modelling tool:

1. **Element types it does not model are dropped outright.**
   `BPMN element <dataStoreReference> 'X' is not part of the model this reader builds and was
   dropped.` The same for `<dataObjectReference>` and `<textAnnotation>`. These are ordinary
   specification §1.6 artifacts and §1.4 data references.

2. **Valid-but-not-executable semantics are dropped.**
   `Intermediate catch event 'X' declares a timer event definition without a <timeDuration>;
   only a one-shot duration catch timer is supported, so it was dropped.` Likewise message
   events whose `messageRef` does not resolve, and catch events with triggers outside
   timer/message/signal. All are legal BPMN to *model*; they are only a problem to *execute*.
   An editor's whole job is to let someone draw a process before it is runnable.

3. **The loss cascades.**
   `Sequence flow 'X' references a dropped element and was dropped with it.` — 11 times in one
   fixture. Dropping one event takes its connectors with it.

Per-fixture dropped/degraded findings:

| fixture | dropped | degraded |
|---|---|---|
| simple-process, order-process, linked-processes, nested-subprocesses, zeebe-process, zeebe-extensions, large-virtualization-stress | 0 | 0 |
| collaboration | 0 | 1 |
| recruitment-and-selection | 2 | 3 |
| smart-parking | 6 | 11 |
| pizza-store | 11 | 7 |
| self-serve-restaurant | 17 | 12 |

### The round trip produces invalid documents

This is the finding that settles it. Because DI is retained while the semantic elements it
points at are dropped, the written document contains `BPMNShape` and `BPMNEdge` entries whose
`bpmnElement` no longer resolves:

| fixture | orphaned shapes | orphaned edges |
|---|---|---|
| pizza-store | 3 | 9 |
| self-serve-restaurant | 5 | 14 |
| smart-parking | 2 | 6 |
| recruitment-and-selection | 0 | 1 |

Specification §10.1 treats an unresolvable `bpmnElement` reference as a document defect, and
§10.2 requires geometry integrity. Matching shape counts looked like fidelity; they were
actually orphaned geometry.

## Why the model cannot simply be adapted

`Bpmn.Model` 0.2.0 is shaped for a token interpreter, not an editor:

- `BpmnElement` has **no container reference**. Specification §6 makes `containerId` load-bearing:
  it is the scope SF-007 (no Sequence Flow across a Sub-Process boundary) is evaluated against.
- Nested Sub-Processes are **hoisted into separate process definitions** and linked by
  `BindingRef`, rather than nested in place. An editor must draw them inside their parent.
- Editor fields are replaced by runtime ones: `ConditionOutcome` instead of §6's
  `conditionExpression`, `MessageName` instead of `messageRef`.
- `BpmnElementTypes` cannot express **Complex Gateway** or **Ad-Hoc Sub-Process** at all;
  Transaction and Event Sub-Process are flags rather than types.
- There are **no types** for Data Objects/Stores, Associations, Data Associations, Text
  Annotations, Groups, ItemDefinitions, Interfaces, Operations, Resources, Correlation, Global
  Tasks, nested LaneSets or Imports.
- `BpmnXmlWriter` exposes `DiagramLayout.Repair` and `DiagramLayout.Route`: it *invents* and
  *reroutes* DI. §4.6 requires undo/redo to restore an exact committed coordinate.

None of this is a defect in the package — it is a good execution-oriented model, MIT-licensed
with no dependencies. It is simply built for a different problem.

## Consequences

- `src/Bpmn/Serialization/BpmnXmlReader.cs` is the reader, over `System.Xml.Linq`.
- The `Bpmn.Interchange` / `Bpmn.Model` package references and their `Directory.Packages.props`
  entries are removed. `InteropOwnershipTests.OnlySerializationReferencesTheInterchangePackages`
  remains as a guard against reintroducing them outside `Serialization/`.
- **M2 must be scoped as owning BPMN XML I/O**, not as wiring up a package. The writer, byte
  fidelity for foreign namespaces, canonical child order and the round-trip fixture suite are all
  still to do.
- The implementation plan's §3 row asserting `Bpmn.Model` is the semantic model of record is
  superseded by this document.

## Reproducing

The throwaway harness lived in the session scratchpad and is not checked in; it read each
fixture with `new BpmnXmlReader().Read(xml)`, wrote it back with
`new BpmnXmlWriter().Write(result.Definitions, result.Bindings)`, grouped
`result.Analysis.Issues` by severity and normalised message, and compared DI element counts and
first-shape bounds between source and result.
