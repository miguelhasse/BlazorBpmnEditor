# BPMN 2.0 SVG Diagram Editor — Implementation Specification

Scope: Process (Orchestration) + Collaboration diagrams per **OMG BPMN 2.0.2** (formal/2013-12-09).
**Choreography diagrams and Choreography Activities/Tasks are explicitly excluded.**
Conversation diagrams are out of scope (they depend on Choreography).

### Compliance boundary

This document defines the target for static diagram-editor compliance; it is not a claim that every
listed capability is already implemented. The shipped implementation and its known limits are
documented separately in the repository's feature and validation documentation. The target covers
the in-scope BPMN 2.0.2 Process and Collaboration metamodel, notation, connection rules, BPMNDI, XML
interchange, and extension preservation. It does not claim to execute BPMN instances. Runtime-only
behavior (token correlation, inclusive-join activation, complex-gateway reset, compensation
ordering, transaction protocol execution, and engine-specific expression evaluation) must be
represented in the model and statically linted where possible, but belongs to an optional
execution/profile layer. A conforming editor must never silently omit or reinterpret a core BPMN
construct merely because it cannot simulate it.

### Rendering-platform and configuration boundary

- The TypeScript canvas implementation must reuse open-source JointJS primitives wherever they meet this contract: graph/paper/cell lifecycle, SVG views and attributes, geometry, anchors and connection points, routers/connectors, tools, highlighters, graph batches, and paper/graph events. Custom TypeScript must specialize those primitives for BPMN rather than recreate a general diagram framework.
- Custom TypeScript is justified where BPMN semantics or a required capability is absent from JointJS OSS, including rule enforcement, containment, Pool/Lane behavior, marker composition, arbitrary-outline grid-snap projection, deterministic label layout, reference-aware clipboard operations, and multi-diagram navigation.
- JointJS+ packages, APIs, widgets, proprietary BPMN shapes, source, and assets are excluded. Visual/interaction observations from JointJS+ demos are behavioral references only.
- Blazor/C# is authoritative for all public editor configuration. It resolves defaults and host overrides, validates them, and sends a versioned configuration snapshot to TypeScript before canvas initialization; later changes are revisioned atomic patches.
- TypeScript may cache the accepted configuration and derive JointJS options from it, but must not maintain competing public defaults or silently accept stale/invalid partial updates. Rejected configuration produces an explicit diagnostic and leaves the last accepted configuration active.
- Editor preferences are distinct from BPMN document state. A configuration change enters document history or XML only when it deliberately changes BPMN semantics or persisted BPMNDI/extension appearance.
- Both C# and TypeScript implementations must remain clean and maintainable as part of feature acceptance. Refactor when responsibilities blur, duplication emerges, abstractions leak, or new behavior would otherwise require a workaround; necessary feature-coupled refactoring is not deferred technical debt.
- Use the latest stable C# and TypeScript syntax supported by the pinned toolchains, favoring modern constructs that improve type safety, immutability, exhaustiveness, resource handling, or clarity. Preview/experimental syntax is opt-in only. Nullable C# and strict TypeScript checks are mandatory; warnings/suppressions must be resolved or narrowly documented.

---

## 1. Element Catalog

### 1.0 Definitions, global elements, and reference catalog

The editor must model these non-drawn semantic objects even when they have no standalone SVG shape:

| Semantic object | Required editor responsibility |
|---|---|
| `Definitions` | Own `id`, required `targetNamespace`, namespace declarations, imports, extensions, root elements, diagrams, relationships, and global ID index. |
| `Process` | Own flow elements, lane sets, artifacts, supported resources, properties, and executable/closed flags. |
| `Collaboration` | Own Participants, Message Flows, artifacts, conversations only if explicitly enabled (Conversation is excluded here), and correlation/document references. |
| `Participant` | Own `processRef`, name, interface/interaction metadata, and Pool DI. A black-box Participant has no visible internal Process flow. |
| `GlobalTask` and global Task subtypes | Definitions referenced by Call Activities; preserve type, input/output sets, resources, and implementation metadata even if no diagram is open for the definition. |
| `Message`, `Signal`, `Error`, `Escalation`, `CorrelationProperty`, `ItemDefinition` | Stable referenced definitions used by event definitions, Message Flows, data associations, and correlation metadata. |
| `Interface`, `Operation`, `Resource`, `ResourceParameter`, `InputSet`, `OutputSet`, `Property` | Required semantic references for complete static modeling of service/send/receive/call tasks and data mappings. |
| `Relationship`, `Import`, `ExtensionDefinition` | Preserve document-level imports, relationships, extension declarations, namespaces, and unknown payloads. |

Every semantic object requires a globally unique XML-safe `id` within `Definitions` (when BPMN requires an ID), a type discriminator, owner/container reference where applicable, and source-location metadata for diagnostics. The editor must maintain an index for ID resolution and update references transactionally when IDs are regenerated.

### 1.1 Flow Objects — Events (circle)

| Category | Border | Icon fill | Valid trigger (EventDefinition) types |
|---|---|---|---|
| Start (none-interrupting only concept doesn't apply) | single thin line | unfilled/white icon | None, Message, Timer, Conditional, Signal, Multiple, Parallel Multiple |
| Start (Event Sub-Process) | single thin (interrupting) / **dashed** (non-interrupting) | unfilled/white icon | Message, Timer, Error, Escalation, Compensation, Conditional, Signal, Multiple, Parallel Multiple (**not None**) |
| Intermediate — Catching (in main flow) | double thin concentric circles | unfilled/white icon | Message, Timer, Conditional, Signal, Link, Multiple, Parallel Multiple |
| Intermediate — Throwing (in main flow) | double thin concentric circles | **filled/black** icon | None, Message, Escalation, Compensation, Signal, Link, Multiple |
| Boundary — Interrupting | double thin, **solid** outline | unfilled/white icon | Message, Timer, Error, Escalation, Cancel*, Conditional, Signal, Compensation, Multiple, Parallel Multiple |
| Boundary — Non-interrupting | double thin, **dashed** outline | unfilled/white icon | Message, Timer, Escalation, Conditional, Signal, Multiple, Parallel Multiple (**not Error, not Cancel, not Compensation**) |
| End | single **thick/bold** line | **filled/black** icon | None, Message, Error, Escalation, Cancel*, Compensation, Signal, Multiple, Terminate |

`*` **Cancel** is legal only on a boundary event attached to a **Transaction** sub-process, or as an End Event *inside* a Transaction sub-process.
**Link** is legal only on Intermediate events (always used in throw/catch pairs, never on Start/End/Boundary).
**Terminate** is legal only on End events.
**Parallel Multiple** is legal only on catching events (Start, Intermediate-catch, Boundary) — never on throwing/End events.
Intermediate Catch Events do not allow `None` (Table 10.89; `None` denotes an Implicit Throw Event).
For Boundary Events, Table 10.92 allows non-interrupting `cancelActivity=false` only for Message,
Timer, Escalation, Conditional, Signal and Multiple triggers whose definitions all allow that
state; Error and Cancel require `true`, while Compensation has no applicable cancellation state.

Icon glyph set: import raw SVG symbol/path data from the pinned [bpmn-io/bpmn-font repository](https://github.com/bpmn-io/bpmn-font), using its SVG font asset (for example `dist/font/bpmn.svg`) only as a build-time source. Do **not** consume the font files, `dist/css/bpmn.css`, CSS pseudo-elements, or a runtime bpmn-io package. The extractor resolves the upstream `bpmn-icon-*` names/codepoints from the raw SVG metadata, converts the selected glyph outlines to normalized standalone SVG path data, and vendors that generated data into this library's icon manifest. Typical mappings include `bpmn-icon-start-event-message`, `bpmn-icon-intermediate-event-catch-timer`, `bpmn-icon-end-event-error`, `bpmn-icon-gateway-xor`, `bpmn-icon-service-task`, and the corresponding marker symbols. The imported geometry is visual source material only: semantic event context, interrupting state, marker legality, fill, and stroke remain controlled by this specification.

### 1.2 Flow Objects — Activities (rounded rectangle)

| Type | Shape rule |
|---|---|
| Task (generic) | rounded rect, no type icon |
| User / Manual / Service / Script / Business Rule / Send / Receive Task | rounded rect + type icon top-left corner (person / hand / gear / script-page / table / filled-envelope / open-envelope) |
| Sub-Process (embedded, expanded) | larger rounded rect containing its own nested graph (own Start/End events) |
| Sub-Process (embedded, collapsed) | rounded rect + small "+" marker bottom-center |
| Event Sub-Process | rounded rect, **dashed border**, always collapsed or expanded, no sequence flow connects to it at all — triggered only by its own (typed, non-None) Start Event |
| Transaction Sub-Process | rounded rect with **double-line (inset) border** |
| Ad-hoc Sub-Process | rounded rect + "~" tilde marker bottom-center |
| Call Activity (calls global Process or global Task) | rounded rect with **thick/bold outer border** |

#### 1.2.1 Embedded (nested) Sub-Process vs. Call Activity (independent, reusable, referenced)

BPMN 2.0 has **two structurally different ways** to represent "a box containing more process logic," and they must not be conflated in the model, the stencil, or the renderer:

| Aspect | Embedded/Nested Sub-Process (`subProcess`, incl. Transaction/Ad-hoc/Event Sub-Process) | Call Activity (`callActivity`) |
|---|---|---|
| Definition ownership | Defined **inline**, as part of the parent `Process`/`Sub-Process`'s own `flowElements` — it has no independent identity outside this one diagram/process. | References a **separate, independently-defined** global `Process` (or global `Task`) via `calledElementRef` — the called definition lives outside this Activity and can be defined once and invoked from many places. |
| Reusability | **Not reusable** — copy/paste duplicates the entire nested graph; each occurrence is a distinct, unrelated set of elements even if visually identical. | **Reusable by reference** — the same `calledElementRef` can be invoked from multiple Call Activities, in the same or different processes/diagrams, all sharing one definition. |
| Visible internal graph | **Yes** — has its own nested `flowElements` (Start/End Events, Tasks, Gateways, Sequence Flows) directly inside this diagram; can be expanded (shown inline) or collapsed (hidden but still owned/editable here). | **No** — the called Process/Task's internal graph is not part of this diagram at all; it is edited (if at all) as its own separate diagram/document. The Call Activity shape itself never contains a nested graph. |
| `containerId` scoping | Introduces a **new nested container scope**: its internal FlowNodes have `containerId` = this Sub-Process's own id; Sequence Flow cannot cross into/out of that scope except via the Sub-Process shape's own boundary or its Boundary Events. | Does **not** introduce a nested scope in this diagram — the Call Activity is a single opaque `FlowNode` in the parent container; there is nothing "inside" it to scope. |
| Sequence Flow attachment | Sequence Flow connects to the Sub-Process shape as a whole (entering/leaving the box); internal flow starts at internal Start Event(s) and ends at internal End Event(s) — these internal events are **not** the same as external Sequence Flow endpoints. | Sequence Flow connects to the Call Activity shape as a whole, exactly like any Task — one opaque box, no internal Start/End Events visible in this diagram. |
| Boundary Events | Fully supported — a Boundary Event attached to an embedded Sub-Process catches exceptions/signals escaping the nested graph. | Fully supported — a Boundary Event attached to a Call Activity catches exceptions/signals escaping the called Process/Task, exactly as if it were a single Task. |
| Typical use case | Grouping/organizing steps that only make sense in this one process (no reuse intended), or implementing Transaction/Ad-hoc/Event-Sub-Process semantics that are inherently local. | Modeling a shared sub-workflow (e.g., "Credit Check," "Send Invoice") that multiple processes/diagrams invoke identically, so it is defined and maintained in exactly one place. |
| Editor stencil/creation | Stencil action "Add embedded Sub-Process" creates a brand-new nested `flowElements` container owned by the current Process/Sub-Process. | Stencil action "Add Call Activity" requires (or prompts to create) a reference to an existing/new **global** Process or Task definition; never auto-generates a nested graph in the current diagram. |
| Rendering marker | Same collapsed `+` / expanded nested-graph convention as any Sub-Process; border style follows the Sub-Process kind (plain, dashed for Event Sub-Process, double-line for Transaction). | Always rendered as a single opaque, always-"collapsed"-looking rounded rectangle with a **thick/bold outer border** — never shows a `+` marker (there is nothing local to expand) and never shows a nested graph inline. |

Editor enforcement:
- Reject any attempt to add semantic `flowElements`/nested Sequence Flow directly inside a Call Activity shape — it must remain a leaf `FlowNode` in this diagram.
- Reject any attempt to set `calledElementRef` on a plain embedded `subProcess` — that field only exists on `callActivity`; converting one kind to the other is a structural transform (copy nested content out to a new global Process, or vice versa), not a simple attribute edit.
- When copy/pasting an embedded Sub-Process, deep-clone its entire nested graph with fresh IDs (no sharing). When copy/pasting a Call Activity, clone only the shape/reference (`calledElementRef` unchanged) — both instances still point at the same shared global definition.
- Deleting an embedded Sub-Process deletes its nested graph. Deleting a Call Activity deletes only the reference/shape in this diagram; the referenced global Process/Task definition is untouched and may still be used elsewhere.
- A collapsed embedded Sub-Process's bottom-center `+` marker is an interactive expand control. A primary-button click/tap on the marker expands that Sub-Process in place; the marker has its own hit target, accessible name ("Expand sub-process"), keyboard activation (`Enter`/`Space`), and must stop propagation so it does not start dragging, connecting, label editing, or another canvas action.
- Double-clicking an expanded embedded Sub-Process's own empty body, border, or header collapses it in place. Double-clicks originating on a nested child, connector, label, marker, resize/link tool, phase/milestone overlay, or active HTML editor must not bubble into collapse. Shape-label editing remains available through direct label double-click/`F2` without collapsing the parent.
- In Edit mode, expand/collapse is an undoable DI/view command that updates `BPMNShape.isExpanded` and the outer shape bounds but never changes the Sub-Process's semantic `flowElements`, IDs, ownership, Sequence Flows, Boundary Events, or child BPMNDI coordinates. In Readonly mode, the same gestures perform a transient local inspection toggle and must not dirty, serialize, or enter document history.
- Expanding materializes and JointJS-embeds the retained child cells/links beneath the Sub-Process. It restores the last expanded bounds in the current session when available; otherwise it computes deterministic bounds from the child BPMNDI content box plus configured padding and minimum size. Collapsing dematerializes/hides nested visual cells from the active paper while retaining semantic content and child BPMNDI for exact re-expansion.
- External connectors remain attached to the Sub-Process boundary in both states. Internal connectors are visible only while expanded. Boundary Events remain attached to and move with the Sub-Process in both states. Nested selection, inline editors, tools, and active gestures are cleared/cancelled safely before collapse.

Additional bottom-center markers (mutually exclusive with each other):
- Loop: circular arrow "↻"
- Sequential Multi-Instance: 3 vertical bars `|||`
- Parallel Multi-Instance: 3 horizontal bars `≡`
- Compensation handler: rewind-arrow ribbon icon

SVG implementation notes for bottom-center Activity markers:
- Reserve a marker band of about 22 px height at the bottom of the Activity shape; markers should be horizontally centered as a group and vertically centered around `y = activityBottom - 11`.
- Loop-characteristic markers are mutually exclusive: Standard Loop, Sequential Multi-Instance, and Parallel Multi-Instance must never render together.
- Compensation can combine with one loop-characteristic marker; when combined, render markers as a centered group with 4-6 px spacing.
- On collapsed Sub-Processes, render the `+` marker at the group center, with loop/compensation markers to its left and the Ad-hoc `~` marker to its right.
- Suggested local marker coordinate system: each marker fits a `14×14` box. Standard Loop uses an arc path plus arrowhead; Sequential Multi-Instance uses three vertical lines; Parallel Multi-Instance uses three horizontal lines; Compensation uses two filled left-pointing triangles; Ad-hoc uses a text/path tilde.

### 1.3 Flow Objects — Gateways (diamond)

| Gateway | Marker inside diamond | Fan-in/out semantics |
|---|---|---|
| Exclusive (XOR) | empty, or "X" (recommended when mixed with other gateway types) | Split: 1 in / N out, each outgoing may carry a condition, exactly one marked **default**; only one outgoing path is taken. Merge: N in / 1 out, no synchronization (pass-through). |
| Event-Based | double-bordered pentagon (octagon look), unfilled event icons implied | 1 in (or 0 in if `instantiate=true`, i.e. it starts the process) / N out. Every outgoing sequence flow MUST target a **catching Intermediate Message/Timer/Signal/Conditional event or a Receive Task** directly (no gateways, no conditions on these flows). First one to trigger wins; others are withdrawn. |
| Parallel Event-Based | as above + "+" inside a circle overlay | Same targeting constraint as Event-Based, but **all** outgoing branches must trigger before continuing (only legal as a **process-instantiating** gateway). |
| Inclusive (OR) | "O" (circle) | Split: 1 in / N out, each outgoing carries a condition, one marked default; **any number ≥1** of true conditions activate in parallel. Merge: N in / 1 out; **synchronizes only the branches that are actually active** (requires runtime token-count awareness). |
| Parallel (AND) | "+" (plus) | Split: 1 in / N out, no conditions allowed, all branches activated. Merge: N in / 1 out, waits for **all** incoming branches (true synchronization barrier). |
| Complex | "*" (asterisk) | Arbitrary custom activation condition on inputs/outputs; both fan-in and fan-out permitted simultaneously. Requires an explicit activation condition expression. |

**Mixed fan-in/fan-out** (multiple incoming AND multiple outgoing on the same gateway) is structurally legal for Exclusive/Inclusive/Complex but **not** for Parallel or Event-Based gateways in typical usage; the spec allows it but best practice (and this editor's *strict mode*, §7) discourages it — recommend splitting into a separate merge gateway followed by a separate split gateway.

### 1.4 Connecting Objects

| Connector | Line style | Source-end marker | Target-end marker | Crosses pool boundary? |
|---|---|---|---|---|
| Sequence Flow (normal) | solid | none | solid filled triangle arrowhead | **No — never** |
| Sequence Flow (conditional, leaving an Activity) | solid | small unfilled diamond | solid filled triangle | No |
| Sequence Flow (default) | solid | small diagonal slash "/" | solid filled triangle | No |
| Message Flow | dashed | small unfilled circle | small unfilled (open, thin-line "V") arrowhead | **Required — must cross** (or attach to a Pool) |
| Association (undirected) | dotted | none | none | n/a (artifact link) |
| Association (directed) | dotted | none | small open thin arrowhead | n/a |
| Data Association | dotted | none | small open thin arrowhead (direction = data flow direction) | n/a |

### 1.5 Swimlanes

- **Pool** = graphical container representing one `Participant` (one Process, or empty = "black box"). Rendered as a large rectangle with a name band on one side.
- **Lane** = subdivision of a Pool for categorization only — **has no effect on sequencing/connection legality**.
- **A Lane requires a Pool.** Every rule below is written in terms of the Pool a Lane divides: Lanes are stacked inside the Pool, a Lane's extent subdivides the Pool's `Bounds`, and a Lane's `isHorizontal` must match its Pool's. A Lane on a Process no `Participant` references has none of those — no rectangle it is a share of, no orientation to inherit, nowhere for a header band — so the editor refuses to create one (`CT-012`) and gives one found on import a `Participant` drawn around the lanes already there, reporting that it did. This is the converse of the rule immediately below: a Pool needs no Lane, but a Lane needs a Pool.
- **A Pool does not require a Lane.** BPMN 2.0.2 defines `Participant.processRef` as optional and `Process.laneSet` with `minOccurs="0"`; therefore both black-box Pools and process-backed Pools with zero LaneSets/Lanes are valid. Lanes are optional partitions introduced only when the modeler needs categorization. The editor must not create a Lane automatically with a Pool or reject/delete the last Lane merely because the Pool would become lane-less. [OMG BPMN 2.0.2 `Semantic.xsd`](https://www.omg.org/spec/BPMN/20100501/Semantic.xsd), `tParticipant` and `tProcess`.
- Both Pool and Lane shapes carry a BPMNDI `isHorizontal` boolean (`BPMNShape.isHorizontal`, `BPMNDI.xsd`). This is purely a **layout/orientation** flag — it never changes containment or connection legality.

#### 1.5.1 Horizontal orientation (`isHorizontal = true`, the conventional default)
- Pool name band is a **vertical strip on the left edge** of the Pool rectangle; band text is rotated 90° (reads bottom-to-top).
- Lanes are stacked **top-to-bottom** inside the Pool, each lane spanning the Pool's full width; a Lane's own name band is a thin vertical strip on the left edge of that lane, with the same rotated-text convention.
- Process flow is conventionally read **left-to-right**; Sequence/Message Flow arrows generally run horizontally between shapes.
- Lane height subdivides the Pool's `Bounds.height`; all lanes in one `LaneSet` share the Pool's `Bounds.width` minus the header band width.

#### 1.5.2 Vertical orientation (`isHorizontal = false`)
- Pool name band is a **horizontal strip on the top edge** of the Pool rectangle; band text reads left-to-right (no rotation).
- Lanes are arranged **left-to-right (as columns)** inside the Pool, each lane spanning the Pool's full height; a Lane's own name band is a thin horizontal strip on the top edge of that lane.
- Process flow is conventionally read **top-to-bottom**; Sequence/Message Flow arrows generally run vertically between shapes.
- Lane width subdivides the Pool's `Bounds.width`; all lanes in one `LaneSet` share the Pool's `Bounds.height` minus the header band height.

#### 1.5.3 Editor rules for orientation
- A Lane's `isHorizontal` **must match** its parent Pool's `isHorizontal` — the editor should set/lock this automatically rather than exposing it as an independent per-lane toggle, to avoid nonsensical mixed layouts.
- Toggling a Pool's orientation is a **pure DI/layout operation**: it must recompute `Bounds` for the Pool, all Lanes, all contained flow-node shapes, and all edge waypoints, but must **not** alter any semantic element, reference, or connection legality — `containerId`/`poolId` scope keys (§2.8) are unaffected.
- Default orientation for a newly created Pool should be horizontal (`isHorizontal = true`); expose an explicit "Rotate to vertical/horizontal" editor action rather than inferring orientation from shape geometry.
- Auto-layout / auto-routing algorithms must branch on `isHorizontal` to choose the dominant flow axis (left-to-right vs top-to-bottom) when placing new shapes and routing new connectors, so newly added elements are consistent with the Pool's existing orientation.
- An empty/black-box Pool (no internal Process/Lanes) still carries `isHorizontal`; it only affects the header band placement since there is no internal content to lay out.

### 1.6 Artifacts

- **Data Object** / **Data Object Reference** / **Data Store Reference**: rectangle (Data Object has folded top-right corner) linked to Activities/Events via Data Association; Text Annotations may describe them through undirected Associations.
- **Group**: rounded-corner **dashed** rectangle, purely visual grouping — is **never** a connector endpoint.
- **Text Annotation**: open bracket "⌐" shape + line, linked via an (undirected) Association. Its connector handle sits 12 diagram units left of the bracket, vertically centered, with the ordinary 7-unit radius.

---

## 2. Connection Rules (Normative)

### 2.1 General graph constraints
1. A **Sequence Flow** connects exactly one source `FlowNode` to exactly one target `FlowNode` (Events, Activities, Gateways only — never Artifacts, Pools, Lanes, or Data Objects).
2. Source and target of a Sequence Flow **MUST belong to the same containing flow-element graph** (the same Process, or the same embedded Sub-Process). It can never cross: a Pool boundary, a Sub-Process boundary (except via a Boundary Event, which is the flow's *virtual* exit point of the activity), or connect into/out of an Event Sub-Process.
3. A **Message Flow** connects two elements that **must reside in different Pools** (or one endpoint is the Pool/Participant shape itself, for "black-box" collaborations). A Message Flow between two elements of the *same* Pool is invalid.
4. Valid Message Flow endpoints: Pool/Participant, Activity (Task/Sub-Process), or a **Message** event (Start, End, Intermediate throw/catch, Boundary). **Gateways can never be Message Flow endpoints.**
5. **Association** connects information and artifacts without control-flow semantics. Text Annotations may describe shapes (except Groups under this editor's contract), including data references, Pools and Lanes, and Sequence Flow or Message Flow lines. Associations have `BaseElement` references (OMG BPMN 2.0.2, §8.4.1); flow-line attachments retain the flow id and their BPMNDI coordinates, not a synthetic shape id. **Data Association** remains the directional data-input/output relationship between data references and Activities/Events. Neither creates control flow.
6. **Non-boundary FlowNode collision rule:** rendered non-boundary Events, Activities, and Gateways must not collide with one another in the same active plane when neither node is an ancestor of the other, including across sibling Pools or Lanes. Collision means that their effective rendered geometry, including the configured interaction clearance, has a positive-area intersection; edge or outline touching is allowed only when the configured clearance is zero. This rule does not treat an ancestor container (Pool, Lane, or expanded Sub-Process) as a peer FlowNode, and it does not prohibit the intentional overlap between a Boundary Event and its host Activity's outline. Boundary Events remain subject to the host-outline rules in §2.5 and may not be used to justify overlap between ordinary FlowNodes.

### 2.2 Start Events
- Incoming Sequence Flow: **0 (forbidden)**.
- Outgoing Sequence Flow: exactly **1** recommended (>1 legal but discouraged — prefer an explicit Parallel Gateway after the start).
- An Event Sub-Process's Start Event has **0 incoming and 0 outgoing Sequence Flow** — it is only reachable via its EventDefinition trigger.

### 2.3 End Events
- Outgoing Sequence Flow: **0 (forbidden)**.
- Incoming Sequence Flow: **≥1** (multiple incoming legal, merges without synchronization — same caveat as Exclusive merge).
- Incoming Message Flow: **0 (forbidden)** — an End Event may never be a Message Flow target.
- Outgoing Message Flow: **0..N allowed** — but if ≥1 outgoing Message Flow exists, the End Event's Result **must** be `Message` or `Multiple`; if **more than one** outgoing Message Flow exists, the Result **must** be `Multiple`.
- **Terminate** End Event: when reached, all other active tokens/branches in the *same* Process instance are immediately discarded (no compensation or event handling is run).
- **Cancel** End Event: legal only inside a **Transaction** Sub-Process; triggers the matching Cancel boundary event on that Transaction and a TransactionProtocol "Cancel" message to involved parties.

### 2.4 Intermediate Events (in main flow, not boundary)
- Catching: 1 incoming, 1 outgoing (standard case).
- Throwing: 1 incoming, 1 outgoing.
- **Link** pair: throwing Link event = 1 incoming Sequence Flow, **0 outgoing**; matching catching Link event (same Link name) = **0 incoming Sequence Flow**, 1 outgoing. They are a non-visual "goto" pair and must never be connected to each other by a drawn Sequence Flow. There may be multiple source (throwing) Links for one target (catching) Link, but never multiple target Links for one source Link.
- Multiple incoming/outgoing Sequence Flows on a normal-flow Intermediate Event are legal but create **uncontrolled flow** (each arriving token independently re-triggers the event rather than synchronizing) — flag as a warning and suggest inserting a Gateway before the event if synchronization is intended.
- **Message Intermediate Event** in normal flow may have **one incoming Message Flow OR one outgoing Message Flow, but never both on the same event.**
- Cancel and Error are **never** valid on a normal-flow (non-boundary) Intermediate Event — they are boundary-only.

### 2.5 Boundary Events
- Attach only to the boundary of an **Activity** (Task or Sub-Process) — never to a Gateway, Event, Pool, or Lane.
- **Geometric position is locked to the host Activity's outline border.** A Boundary Event's shape center must sit exactly on the host's rendered outline (rectangle perimeter, including rounded corners) — never floating inside, outside, or off the border. It is never positioned by free (x,y) placement independent of the host.
- Dragging a Boundary Event repositions it **along** the host's outline only (nearest valid outline point to the pointer, matching the §4.6 outline-anchor projection rule); it cannot be dragged off the border onto the canvas or onto a different, unattached position. Detaching/reattaching to a different host Activity is a distinct re-parent gesture, not a side effect of ordinary dragging.
- Moving or resizing the host Activity must move the Boundary Event with it, preserving its relative position along the (possibly reshaped) outline so it continues to render exactly on the border after the host's geometry changes.
- Incoming Sequence Flow: **0 (forbidden)** — the "trigger" is implicit from the host activity.
- Outgoing Sequence Flow: **≥1** (typically exactly 1).
- **Cancel** boundary type: legal only when host is a **Transaction** Sub-Process.
- **Compensation** boundary type: outgoing connection to its handler is an **Association** (dashed, undirected) to the compensation Activity, not a Sequence Flow; the compensation handler activity itself must not have any other incoming Sequence Flow from the main flow.

### 2.6 Gateways
- Exclusive/Inclusive/Complex: outgoing conditional flows require a condition expression except at most **one** flow marked **isDefault** (no condition — any condition present on it is ignored per spec and SHOULD be rejected/cleared by the editor). If a split has no default and no statically-provable always-true condition, warn that a runtime exception is possible if no branch evaluates true.
- Parallel: no condition expressions and no `default` attribute permitted on any outgoing flow; every outgoing flow always receives a token (fork); the gateway waits for **all** incoming flows before proceeding (join) — true synchronization barrier.
- Inclusive (merge): synchronizes only the branches that are *actually active* at runtime (not simply "all declared incoming flows") — this cannot be fully validated statically; treat convergence correctness as a runtime concern and only lint for structurally obvious cases (e.g., a single incoming flow, which makes the gateway redundant).
- Complex: has an `activationCondition` Expression governing synchronization instead of a fixed rule; require this expression to be present whenever the gateway has more than one incoming flow (unless the chosen modeling profile explicitly allows implicit behavior); may also carry a `default` outgoing flow with the same "no condition" rule as Exclusive/Inclusive.
- Event-Based: outgoing flows forbidden to carry conditions or a `default`; the gateway **must have ≥2 outgoing Sequence Flows**; each outgoing flow's target restricted to {Intermediate Catch Event (Message/Signal/Timer/Conditional only — never Error/Cancel/Compensation/Link/None), Receive Task} — connecting to a Task, Gateway, or None-event is invalid.
  - **Homogeneity constraint**: within one Event-Based Gateway's outgoing configuration, Message Intermediate Catch Events and Receive Tasks **must not be mixed** — pick one style per gateway.
  - Receive Tasks used in this configuration **must not** have any attached (boundary) Intermediate Events.
  - Every target element in the configuration **must not** have any additional incoming Sequence Flow besides the one from the Event-Based Gateway (otherwise the race-condition semantics are ambiguous).
- A gateway with `instantiate = true` (Event-Based, or Parallel-Event-Based) must have **0 incoming** Sequence Flow — it is itself a process start.
- `eventGatewayType = Parallel` is only legal when `instantiate = true` (a mid-process parallel merge of events should use a standard Parallel Gateway downstream of catch events, not this variant). When used, all configured branches must eventually trigger (their messages must share correlation information) rather than racing to a single winner.
- **Mixed fan-in/fan-out** (a gateway with both multiple incoming *and* multiple outgoing Sequence Flows) is structurally legal for Exclusive/Inclusive/Complex but not idiomatic for Parallel/Event-Based; treat as a warning (not an error) recommending a separate merge-then-split pair for clarity.

### 2.7 Sub-Process containment
- All Sequence Flow between elements nested in an embedded Sub-Process must stay nested; the Sub-Process's own Start/End Events bound that nested graph.
- **Embedded (nested) Sub-Process** = its `flowElements` are owned inline by this Process/Sub-Process; it has no independent, reusable identity — it cannot be invoked from anywhere else, and copying it duplicates the nested graph entirely (fresh IDs, no sharing).
- **Call Activity** = an **independent, reusable, referenced** Activity: it has **no nested graph in this diagram** at all. It is an opaque `FlowNode` whose `calledElementRef` points at a separate, independently-defined global `Process` or global `Task`, which may be invoked by any number of Call Activities across any number of processes/diagrams. Sequence Flow connects to/from the Call Activity shape itself only — never to anything "inside" it, because there is nothing inside it in this diagram.
- These two are **not interchangeable representations of the same thing**: an embedded Sub-Process cannot carry `calledElementRef`, and a Call Activity cannot carry local `flowElements`/`laneSets`. Converting between them is a structural transform (extract nested content into a new global Process definition and replace with a Call Activity referencing it, or inline-copy a called Process's definition into a new embedded Sub-Process), never a simple attribute toggle.
- Deleting an embedded Sub-Process removes its nested graph permanently (nothing else could reference it). Deleting a Call Activity removes only this reference/shape; the shared global Process/Task definition it points to is unaffected and may still be used by other Call Activities.

### 2.8 Pools / Lanes
- Pool-without-Lanes is valid. A Pool may be black-box (`processRef` absent), or may reference a Process whose `laneSet` collection is empty. Lane presence is never a prerequisite for placing FlowNodes in a process-backed Pool.
- Deleting the last Lane must retain the Pool and Process. Its former `flowNodeRefs` are removed with the Lane, while the FlowNodes remain owned by the Process at their existing DI positions (subject only to normal containment/layout adjustment); the editor must not delete or re-parent those FlowNodes.
- Sequence Flow: legal only *within* one Pool (may freely cross Lane boundaries inside that Pool — Lanes are visual classification only and never change Sequence Flow legality).
- Message Flow: illegal *within* one Pool; **required** to cross Pools (or terminate at a Pool header for a black-box Participant).
- An empty Pool (no internal nodes) may only be a Message Flow endpoint, never a Sequence Flow endpoint.
- Maintain two independent scope keys per `FlowNode`: `containerId` (nearest Process/embedded Sub-Process — governs Sequence Flow legality) and `poolId`/`participantId` (nearest Pool — governs Message Flow legality). Do not conflate the two: a Sequence Flow check compares `containerId`; a Message Flow check compares `poolId`.

### 2.8.1 JointJS container and grouping hierarchy

The live canvas must use open-source JointJS embedding/grouping support as the authoritative **visual hierarchy projection**. It must not maintain a second custom parent/child movement tree. BPMN semantics remain authoritative; JointJS embedding mirrors the semantic/DI hierarchy and is rebuilt or repaired from it.

- Enable JointJS embedding behavior on the paper and use JointJS cell embedding APIs, embedded-cell traversal, graph batches, and embedding validation for container movement, deep selection, deletion, z-order, fit/resize calculations, and reparenting.
- Every rendered cell has at most one direct JointJS parent and the embedding graph must be acyclic. A parent must be an allowed visual container for that child; invalid or cyclic embedding is rejected before mutation.
- The direct visual parent is the **nearest applicable rendered container**, producing this hierarchy:
  - active diagram/plane cells with no visual container are graph roots;
  - top-level Lanes are embedded in their Pool;
  - nested Lanes are embedded in their parent Lane;
  - a FlowNode or Artifact directly in a process-backed Pool is embedded in its deepest applicable Lane, or directly in the Pool when it has no Lane membership;
  - an expanded embedded Sub-Process is embedded in its nearest Lane/Pool/expanded Sub-Process, and its internal rendered content is embedded directly beneath that Sub-Process or further nested embedded Sub-Processes;
  - a Boundary Event is embedded in its attached Activity for visual movement/attachment while `attachedToRef` remains the semantic authority;
  - other editor-owned auxiliary cells, when represented as cells rather than owner markup, are embedded in their owning Pool/shape and marked non-semantic.
- Lane embedding represents visual organization and membership, **not FlowElement ownership**. A FlowNode embedded in a Lane remains semantically owned by the containing Process/Sub-Process; `Lane.flowNodeRef` records Lane membership. JointJS parent IDs must never be serialized as invented BPMN ownership.
- A BPMN **Group** (`artifact:Group`) is a non-containing visual categorization artifact. Cells geometrically inside or categorized by a Group must not be JointJS-embedded into it, must not move/delete with it, and do not acquire a new semantic owner. Multi-selection wrappers and temporary selection groups are also transient UI state, not model hierarchy.
- Collapsed Sub-Processes and Call Activities are opaque leaves in the active plane and must not have visible embedded process-content cells. A collapsed embedded Sub-Process may retain hidden semantic content, but that content is rendered/embedded only when expanded. A Call Activity never embeds content from its referenced global Process.
- Expansion/collapse follows §1.2.1: clicking the collapsed `+` materializes and embeds descendants; double-clicking the expanded container body/header removes nested visuals from the active paper. Each transition occurs in one JointJS graph batch and hierarchy parity is checked afterward.
- A Sequence Flow whose endpoints share a FlowElementsContainer is embedded in the rendered cell for that lowest common container when one exists, so explicit vertices and labels move with the container. Associations/Data Associations follow their semantic scope. Cross-Pool Message Flows remain at the Collaboration/plane root and are never embedded in either Pool. Link embedding never changes semantic endpoints or legality.
- Container translation uses JointJS deep embedded-cell behavior inside one graph batch. Descendants must move exactly once; internal link vertices/labels move with the container; cross-container links reroute or adjust only their affected endpoint. Do not independently translate descendants after JointJS has already moved them.
- Drop/reparent is transactional: identify the candidate parent with JointJS hit-testing, choose the deepest eligible container, validate BPMN containment/Lane membership/connection scope first, then update semantic references, BPMNDI, and JointJS embedding in one command. Rejection restores the original semantic parent, embedding parent, coordinates, memberships, links, and z-order.
- Import/load reconstructs embedding deterministically from semantic ownership, Lane membership, `attachedToRef`, link scope, and BPMNDI. Export derives BPMN solely from semantic/DI state, never by interpreting arbitrary JointJS embedding.
- After load and every containment command, assert semantic/visual hierarchy parity. Missing parents, illegal parent types, cycles, duplicate membership, incorrect lowest-container link placement, and Group-as-parent are editor errors, not tolerated display differences.

### 2.9 Data Associations
- A **DataAssociation** (Data Input/Output Association) moves data between Data Objects/Data Stores/Properties and Activity/Event inputs/outputs; **no token/control-flow semantics** — it must never be treated as, or substitute for, a Sequence Flow.
- **Data Input Association**: source = Data Object Reference / Data Store Reference, target = Activity or a **Throwing** Event (data pushed into the message being thrown).
- **Data Output Association**: source = Activity or a **Catching** Event (data pulled from a received message), target = Data Object Reference / Data Store Reference.
- If the association has **no transformation expression**, it **must have exactly one source** (its contents are copied verbatim into the target). If a transformation expression is present, zero-to-many sources are allowed (used or not inside the expression).
- If the source and target item definitions differ, a transformation expression is **required** to reconcile them.
- Visually, a Data Association connected to a Sequence Flow (data hanging off a flow line) is a **DI-only shortcut** representing two normalized Data Associations in the underlying model — resolve this at the DI layer, not by inventing a new semantic connector type.

---

## 3. Connection Compatibility Matrix

Matrices below describe **endpoint type compatibility only**. Scope, event context, gateway configuration, default/condition attributes, containment, pool identity, item definitions, and cardinality are additional predicates and must be enforced by §2 and §5; a `Yes` cell never overrides those rules.

### 3.1 Sequence Flow (`SF`)

Sequence Flow source and target must be FlowNodes in the same `containerId` (same Process or embedded Sub-Process). `Yes*` means the endpoint is generally compatible but has a special event/gateway rule.

| Source \ Target | Start Event | Intermediate normal | End Event | Boundary Event | Activity / embedded Sub-Process | Gateway | Event Sub-Process | Artifact / Data | Pool / Lane |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Start Event | No | Yes | Yes | No | Yes | Yes | No | No | No |
| Intermediate Catch/Throw | No | Yes* | Yes | No | Yes | Yes | No | No | No |
| Boundary Event | No | Yes | Yes | No | Yes | Yes | No | No | No |
| End Event | No | No | No | No | No | No | No | No | No |
| Activity / embedded Sub-Process | No | Yes | Yes | No | Yes | Yes | No | No | No |
| Gateway | No | Yes* | Yes | No | Yes* | Yes* | No | No | No |
| Event Sub-Process | No | No | No | No | No | No | No | No | No |
| Artifact / Data | No | No | No | No | No | No | No | No | No |
| Pool / Lane | No | No | No | No | No | No | No | No | No |

`*` Special cases: Start has no incoming SF; End has no outgoing SF; Boundary has no incoming SF; Link throw has no outgoing SF; Link catch has no incoming SF; Event Sub-Process Activities are not endpoints; Event-Based Gateway targets are restricted to configured catching Events/Receive Tasks; Parallel Gateway outgoing flows cannot have conditions; compensation Activities are not normal-flow endpoints.

### 3.2 Message Flow (`MF`)

Message Flow endpoints must be InteractionNodes (Participant/Pool, Activity, or Event) in **different Participants/Pools**. Gateway, Lane, Data Object/Store, Group, Text Annotation, and Association are never MF endpoints. `Yes*` requires a message-capable event context.

| Source \ Target | Pool / Participant (other) | Activity (other Pool) | Catching Message Event (other Pool) | Start Event (other Pool) | End Event | Boundary Event | Gateway | Lane / Artifact / Annotation | Same-Pool object |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Pool / Participant | Yes | Yes | Yes | Yes* | No | No | No | No | No |
| Activity / Call Activity | Yes | Yes | Yes | Yes* | No | No | No | No | No |
| Message Intermediate Throw Event | Yes | Yes | Yes | Yes* | No | No | No | No | No |
| Message End Event | Yes | Yes | Yes | Yes* | No | No | No | No | No |
| Message Start Event | No | No | No | No | No | No | No | No | No |
| Boundary Message Catch Event | No | No | No | No | No | No | No | No | No |
| Gateway | No | No | No | No | No | No | No | No | No |
| Lane / Artifact / Annotation | No | No | No | No | No | No | No | No | No |

`*` An Event endpoint must have the correct catch/throw/message definition. An End Event may source MF but may never target it; an Intermediate Message Event may have one incoming MF or one outgoing MF, never both. A Pool endpoint represents the Participant/Pool boundary, including a black-box Pool.

### 3.3 Association (`A`)

Association is a dotted artifact/information link, not control flow. It may be undirected, directed, or bidirectional according to `associationDirection`.

| Source \ Target | Activity / Event / Gateway | Data Object / Data Store | Text Annotation | Group | Sequence/Message Flow edge |
|---|---:|---:|---:|---:|---:|
| Activity / Event / Gateway | Profile-dependent / no ordinary A between two Flow Objects | Yes (information/artifact association only) | Yes | No | Yes* |
| Data Object / Data Store | Yes | No | Yes | No | Yes* |
| Text Annotation | Yes | Yes | Yes | No | Yes |
| Group | No | No | No | No | No |
| Sequence/Message Flow edge | Yes* | Yes* | Yes | No | No |

`Yes*` is a DI/notation case and must not be confused with a new semantic endpoint type. A Data Association shortcut visually attached to a Sequence Flow is normalized to semantic Data Associations; a Compensation Association must originate at a Compensation Boundary Event and target a valid compensation Activity.

The annotation connector gesture supports only Sequence Flow and Message Flow as line endpoints;
it does not enable ordinary shape-to-line Associations, Association chains or Data Association
targets. A release projects to the nearest point on the flow path. Its relative path length follows
flow movement, resizing and rerouting, and is reconstructed from standard BPMNDI waypoints after
reload. Both the flow and its dependent Association geometry participate in the same undo entry.

### 3.4 Data Association (`DA`)

Data Associations are directional and have distinct input/output endpoint rules. Use `DataObjectReference`/`DataStoreReference` (or valid Property references) rather than the global Data Object/Data Store definition as the diagram endpoint.

| Data Association kind | Source | Target | Required semantic rule |
|---|---|---|---|
| Data Input Association | Data Object Reference, Data Store Reference, or Property | Activity or Throwing Event | Data is supplied to the Activity/Event input. |
| Data Output Association | Activity or Catching Event | Data Object Reference, Data Store Reference, or Property | Data is produced/pulled from the Activity/Event output. |
| Invalid DA | Gateway, Lane, Pool, Group, Text Annotation, Start/End event in an incompatible direction, or arbitrary FlowNode-to-FlowNode | Any incompatible endpoint | Reject with DA-005/DA-006. |

Additional DA predicates:
- Without a transformation, `sourceRefs.length` must equal exactly one.
- With a transformation, zero-to-many sources are allowed.
- Mismatched source/target item definitions require a transformation expression.
- Data Association never crosses the Process/Sub-Process semantic scope implicitly; references and visibility must resolve in the owning model.

### 3.5 Matrix interpretation and connector creation

- A connector gesture must select one connector kind before target highlighting. Do not infer `SF`, `MF`, `A`, or `DA` solely from the source/target shape.
- Candidate highlighting runs the full validator, not just the matrix lookup.
- Matrix `No` is a hard rejection. Matrix `Yes` proceeds to scope, direction, cardinality, event-definition, gateway, containment, and profile checks.
- A connector cannot be committed with an unresolved endpoint, wrong container, wrong Pool identity, or missing required definition/reference.
- The matrix is symmetric only where explicitly stated; Association and Data Association direction are not interchangeable.

---

## 4. Marker & Decorator Geometry (SVG rendering)

Recommended base grid: 100×80 px per Task/Gateway/Event bounding box (BPMN convention: Events Ø=36px, Gateways diamond 50×50px, Tasks 100×80px rounded rect rx=10).

```xml
<defs>
  <!-- Sequence Flow arrowhead: solid filled triangle -->
  <marker id="sf-arrow" viewBox="0 0 10 10" refX="9" refY="5"
          markerWidth="8" markerHeight="8" orient="auto-start-reverse">
    <path d="M0,0 L10,5 L0,10 Z" fill="#000"/>
  </marker>

  <!-- Message Flow arrowhead: open/unfilled -->
  <marker id="mf-arrow" viewBox="0 0 10 10" refX="9" refY="5"
          markerWidth="8" markerHeight="8" orient="auto-start-reverse">
    <path d="M0,0 L10,5 L0,10" fill="none" stroke="#000" stroke-width="1.2"/>
  </marker>

  <!-- Message Flow source: open circle -->
  <marker id="mf-circle" viewBox="0 0 10 10" refX="5" refY="5"
          markerWidth="6" markerHeight="6">
    <circle cx="5" cy="5" r="4" fill="#fff" stroke="#000" stroke-width="1"/>
  </marker>

  <!-- Conditional Sequence Flow source: unfilled diamond -->
  <marker id="cond-diamond" viewBox="0 0 10 10" refX="9" refY="5"
          markerWidth="8" markerHeight="8">
    <path d="M0,5 L5,0 L10,5 L5,10 Z" fill="#fff" stroke="#000" stroke-width="1"/>
  </marker>

  <!-- Data/Text Association arrowhead: open thin arrow -->
  <marker id="assoc-arrow" viewBox="0 0 10 10" refX="9" refY="5"
          markerWidth="6" markerHeight="6" orient="auto-start-reverse">
    <path d="M2,1 L9,5 L2,9" fill="none" stroke="#000" stroke-width="1"/>
  </marker>
</defs>
```

| Line kind | `stroke-dasharray` | markers |
|---|---|---|
| Sequence Flow | none (solid) | `marker-end="url(#sf-arrow)"` (+ `marker-start="url(#cond-diamond)"` if conditional-from-activity) |
| Default Sequence Flow | none | slash mark = separate small `<line>` at 45° near source, plus `sf-arrow` at end |
| Message Flow | `5,4` | `marker-start="url(#mf-circle)"`, `marker-end="url(#mf-arrow)"` |
| Association | `2,3` | none (undirected) or `marker-end="url(#assoc-arrow)"` (directed/data) |
| Non-interrupting Boundary Event outline | `4,3` on the outer circle stroke | n/a |
| Event Sub-Process border | `6,4` | n/a |

Gateway symbol paths (drawn centered in the 50×50 diamond, ~20×20 glyph):
- X (Exclusive): two crossed diagonal lines.
- O (Inclusive): circle, stroke-width 3, no fill.
- + (Parallel): plus sign, stroke-width 3.
- \* (Complex): 6-point asterisk.
- Event-based: circle-of-pentagons overlay, glyph unfilled.

### 4.1 Gateway symbol SVG paths

The existing gateway outer diamond remains editor-owned, but every inner gateway glyph must be resolved from the generated bpmn-font registry (`bpmn-icon-gateway-none`, `bpmn-icon-gateway-xor`, `bpmn-icon-gateway-or`, `bpmn-icon-gateway-parallel`, `bpmn-icon-gateway-complex`, and `bpmn-icon-gateway-eventbased`). The hand-authored X, O, plus, asterisk, pentagon, and double-ring path examples below are legacy visual guidance only and must not be used as a second symbol catalog. The registry provides normalized path data, viewBox, baseline, and alignment; semantic gateway attributes select the legal symbol.

Render every Gateway as a 50×50 diamond using the same outer path, then place the type glyph in a local `50×50` coordinate system. The glyph should inherit `currentColor`, use `vector-effect="non-scaling-stroke"`, and avoid `fill` unless the symbol explicitly requires it.

```xml
<!-- Common Gateway outer diamond -->
<path class="bpmn-gateway-shape"
      d="M25 0 L50 25 L25 50 L0 25 Z"
      fill="#fff" stroke="#000" stroke-width="2"/>

<!-- Exclusive Gateway marker: optional X -->
<path class="bpmn-gateway-exclusive-marker"
      d="M17 17 L33 33 M33 17 L17 33"
      fill="none" stroke="currentColor" stroke-width="3"
      stroke-linecap="round" vector-effect="non-scaling-stroke"/>

<!-- Inclusive Gateway marker: O -->
<circle class="bpmn-gateway-inclusive-marker"
        cx="25" cy="25" r="9"
        fill="none" stroke="currentColor" stroke-width="3"
        vector-effect="non-scaling-stroke"/>

<!-- Parallel Gateway marker: plus -->
<path class="bpmn-gateway-parallel-marker"
      d="M25 14 L25 36 M14 25 L36 25"
      fill="none" stroke="currentColor" stroke-width="3"
      stroke-linecap="round" vector-effect="non-scaling-stroke"/>

<!-- Complex Gateway marker: asterisk -->
<path class="bpmn-gateway-complex-marker"
      d="M25 13 L25 37 M13 25 L37 25 M16.5 16.5 L33.5 33.5 M33.5 16.5 L16.5 33.5"
      fill="none" stroke="currentColor" stroke-width="2.5"
      stroke-linecap="round" vector-effect="non-scaling-stroke"/>

<!-- Event-Based Gateway marker: double circle + pentagon-like event marker -->
<circle class="bpmn-gateway-eventbased-outer" cx="25" cy="25" r="13"
        fill="none" stroke="currentColor" stroke-width="1.8"/>
<circle class="bpmn-gateway-eventbased-inner" cx="25" cy="25" r="10"
        fill="none" stroke="currentColor" stroke-width="1.4"/>
<path class="bpmn-gateway-eventbased-pentagon"
      d="M25 15 L34.5 22 L31 34 L19 34 L15.5 22 Z"
      fill="none" stroke="currentColor" stroke-width="1.6"/>

<!-- Parallel Event-Based Gateway marker: double circle + plus -->
<circle class="bpmn-gateway-parallel-eventbased-outer" cx="25" cy="25" r="13"
        fill="none" stroke="currentColor" stroke-width="1.8"/>
<circle class="bpmn-gateway-parallel-eventbased-inner" cx="25" cy="25" r="10"
        fill="none" stroke="currentColor" stroke-width="1.4"/>
<path class="bpmn-gateway-parallel-eventbased-plus"
      d="M25 17 L25 33 M17 25 L33 25"
      fill="none" stroke="currentColor" stroke-width="2.4"
      stroke-linecap="round" vector-effect="non-scaling-stroke"/>
```

Gateway rendering rules:
- Keep the outer diamond identical for all Gateway kinds; only the inner glyph varies.
- Exclusive Gateway may render empty or with X, but the diagram should be consistent. If any Exclusive Gateway uses X in a diagram, prefer rendering all Exclusive Gateways with X.
- Event-Based Gateway and Parallel Event-Based Gateway must use the double-ring event-style marker; the Parallel variant replaces the pentagon/race marker with a plus-style parallel marker.
- Do not encode gateway semantics in SVG classes alone. SVG glyph choice is derived from semantic `gatewayDirection`, `eventGatewayType`, `instantiate`, and the element type.

### 4.2 Intermediate and Boundary Event SVG paths

Event trigger glyphs must be resolved from the generated bpmn-font registry, using the exact context-specific names (`start-event-*`, `intermediate-event-catch-*`, `intermediate-event-throw-*`, `end-event-*`, and `intermediate-event-catch-non-interrupting-*`). The outer Start/Intermediate/End/Boundary rings remain editor-owned because ring thickness and dashed/solid state encode event context. The hand-authored event glyph paths below are legacy visual guidance only and must not be emitted by the renderer.

Use the same normalized 36×36 event box for Start/Intermediate/End/Boundary Events. Place the event center at `(18,18)` and keep the icon glyph inside a 18×18 box centered at `(18,18)` (`x=9..27`, `y=9..27`). Use `currentColor` for strokes and derive icon fill from catch/throw/end semantics.

```xml
<!-- Intermediate Event base: double thin ring -->
<g class="bpmn-event bpmn-intermediate-event">
  <circle cx="18" cy="18" r="17" fill="#fff" stroke="#000" stroke-width="1.5"/>
  <circle cx="18" cy="18" r="14" fill="none" stroke="#000" stroke-width="1.2"/>
</g>

<!-- Boundary Event base: same double ring, attached to host activity boundary (center always on the host's outline, per §2.5) -->
<g class="bpmn-event bpmn-boundary-event">
  <circle cx="18" cy="18" r="17" fill="#fff" stroke="#000" stroke-width="1.5"/>
  <circle cx="18" cy="18" r="14" fill="none" stroke="#000" stroke-width="1.2"/>
</g>

<!-- Non-interrupting Boundary Event: dash both rings -->
<g class="bpmn-event bpmn-boundary-event bpmn-non-interrupting">
  <circle cx="18" cy="18" r="17" fill="#fff" stroke="#000" stroke-width="1.5" stroke-dasharray="4 3"/>
  <circle cx="18" cy="18" r="14" fill="none" stroke="#000" stroke-width="1.2" stroke-dasharray="4 3"/>
</g>

<!-- Message icon: catch = unfilled envelope, throw/end = filled envelope -->
<path class="bpmn-event-message-envelope"
      d="M9 12 H27 V24 H9 Z M9 12 L18 19 L27 12"
      fill="var(--bpmn-event-icon-fill, #fff)"
      stroke="currentColor" stroke-width="1.4"
      stroke-linejoin="round" vector-effect="non-scaling-stroke"/>

<!-- Timer icon -->
<circle class="bpmn-event-timer-face" cx="18" cy="18" r="8"
        fill="none" stroke="currentColor" stroke-width="1.4"/>
<path class="bpmn-event-timer-hands"
      d="M18 18 L18 12 M18 18 L23 20"
      fill="none" stroke="currentColor" stroke-width="1.4"
      stroke-linecap="round"/>

<!-- Error icon -->
<path class="bpmn-event-error"
      d="M20 8 L11 20 H17 L15 28 L25 15 H19 Z"
      fill="var(--bpmn-event-icon-fill, #000)"
      stroke="currentColor" stroke-width="1.1"
      stroke-linejoin="round"/>

<!-- Escalation icon -->
<path class="bpmn-event-escalation"
      d="M18 8 L28 26 L18 21 L8 26 Z"
      fill="var(--bpmn-event-icon-fill, #fff)"
      stroke="currentColor" stroke-width="1.4"
      stroke-linejoin="round"/>

<!-- Cancel icon -->
<path class="bpmn-event-cancel"
      d="M11 11 L25 25 M25 11 L11 25"
      fill="none" stroke="currentColor" stroke-width="3"
      stroke-linecap="round"/>

<!-- Compensation icon -->
<path class="bpmn-event-compensation"
      d="M11 18 L19 12 V24 Z M19 18 L27 12 V24 Z"
      fill="var(--bpmn-event-icon-fill, #000)"
      stroke="currentColor" stroke-width="1.2"
      stroke-linejoin="round"/>

<!-- Signal icon -->
<path class="bpmn-event-signal"
      d="M18 9 L28 26 H8 Z"
      fill="var(--bpmn-event-icon-fill, #fff)"
      stroke="currentColor" stroke-width="1.4"
      stroke-linejoin="round"/>

<!-- Conditional icon -->
<path class="bpmn-event-conditional-page"
      d="M11 8 H23 L27 12 V28 H11 Z M23 8 V12 H27 M14 16 H24 M14 20 H24 M14 24 H21"
      fill="var(--bpmn-event-icon-fill, #fff)"
      stroke="currentColor" stroke-width="1.2"
      stroke-linejoin="round"/>

<!-- Link icon -->
<path class="bpmn-event-link"
      d="M9 18 H24 M20 13 L27 18 L20 23"
      fill="none" stroke="currentColor" stroke-width="2"
      stroke-linecap="round" stroke-linejoin="round"/>

<!-- Multiple icon -->
<path class="bpmn-event-multiple"
      d="M18 8 L27 15 L24 27 H12 L9 15 Z"
      fill="var(--bpmn-event-icon-fill, #fff)"
      stroke="currentColor" stroke-width="1.4"
      stroke-linejoin="round"/>

<!-- Parallel Multiple icon -->
<path class="bpmn-event-parallel-multiple"
      d="M18 9 L18 27 M9 18 L27 18"
      fill="none" stroke="currentColor" stroke-width="2.4"
      stroke-linecap="round"/>

<!-- Terminate icon for End Event -->
<circle class="bpmn-event-terminate" cx="18" cy="18" r="8" fill="currentColor"/>
```

Event rendering rules:
- Intermediate Events always render as two thin concentric rings. Boundary Events also render as two thin concentric rings, but their `g` is positioned so the event center lies on the host Activity border.
- Interrupting Boundary Events use solid rings; non-interrupting Boundary Events use dashed rings on both circles and map to `cancelActivity=false`.
- Catching Intermediate/Boundary/Start icons are unfilled (`--bpmn-event-icon-fill: #fff`); Throwing Intermediate and End icons are filled (`--bpmn-event-icon-fill: currentColor`) except icons whose notation is inherently stroked (Timer, Link, Parallel Multiple).
- Error and Cancel may render on Boundary Events but must not render as normal-flow Intermediate Events; validation must reject that semantic state before rendering/export.
- Compensation Boundary Events render as solid boundary rings and do not get outgoing Sequence Flow; their handler link uses Association notation.
- Link Events must render only as Intermediate Events and must be paired by name in the same Process scope.

### 4.3 Additional bottom-center Activity marker SVG paths

Activity markers must resolve from the generated bpmn-font registry: `bpmn-icon-loop-marker`, `bpmn-icon-parallel-mi-marker`, `bpmn-icon-sequential-mi-marker`, `bpmn-icon-compensation-marker`, `bpmn-icon-sub-process-marker`, and `bpmn-icon-ad-hoc-marker`. The hand-authored marker paths below are legacy visual guidance only and must not be emitted by the renderer.

Render bottom-center markers in a local `14×14` glyph box. For an Activity with bounds `{x,y,width,height}`, place the marker group at `translate(x + width / 2 - groupWidth / 2, y + height - 20)`.

```xml
<!-- Collapsed Sub-Process marker -->
<rect class="bpmn-marker-collapsed-box" x="1" y="1" width="12" height="12"
      fill="#fff" stroke="currentColor" stroke-width="1"/>
<path class="bpmn-marker-collapsed-plus"
      d="M7 4 L7 10 M4 7 L10 7"
      fill="none" stroke="currentColor" stroke-width="1.5"
      stroke-linecap="round"/>

<!-- Standard Loop marker -->
<path class="bpmn-marker-loop"
      d="M10.5 4.5 A4.5 4.5 0 1 0 11 10 M11 10 L11 6.5 M11 10 L7.5 10"
      fill="none" stroke="currentColor" stroke-width="1.4"
      stroke-linecap="round" stroke-linejoin="round"/>

<!-- Multi-Instance Parallel marker: three vertical bars -->
<path class="bpmn-marker-mi-parallel"
      d="M4 2 L4 12 M7 2 L7 12 M10 2 L10 12"
      fill="none" stroke="currentColor" stroke-width="1.6"
      stroke-linecap="round"/>

<!-- Multi-Instance Sequential marker: three horizontal bars -->
<path class="bpmn-marker-mi-sequential"
      d="M2 4 L12 4 M2 7 L12 7 M2 10 L12 10"
      fill="none" stroke="currentColor" stroke-width="1.6"
      stroke-linecap="round"/>

<!-- Compensation marker -->
<path class="bpmn-marker-compensation"
      d="M2 7 L7 3 V11 Z M7 7 L12 3 V11 Z"
      fill="currentColor" stroke="currentColor" stroke-width="0.8"
      stroke-linejoin="round"/>

<!-- Ad-hoc Sub-Process marker -->
<path class="bpmn-marker-adhoc"
      d="M2 8 C4 4, 6 4, 8 8 S12 12, 14 8"
      fill="none" stroke="currentColor" stroke-width="1.5"
      stroke-linecap="round"/>
```

Bottom marker composition rules:
- `StandardLoopCharacteristics`, `MultiInstanceLoopCharacteristics.isSequential=false`, and `MultiInstanceLoopCharacteristics.isSequential=true` are mutually exclusive and must render at most one loop/MI marker.
- Compensation (`isForCompensation=true`) may render together with exactly one loop/MI marker.
- Collapsed Sub-Process `+` may render with loop/MI/compensation; keep `+` closest to center, compensation/loop markers to the left, and Ad-hoc `~` to the right.
- Ad-hoc marker applies only to Ad-hoc Sub-Processes and may appear with collapsed marker where the shape is both collapsed and ad-hoc.
- Marker layout must be deterministic: build a marker list in semantic order `[compensation, loopOrMI, collapsed, adHoc]`, measure as `14px * count + 5px * gaps`, then center the group.

### 4.4 Task type symbol SVG paths

Task symbols must resolve from the generated bpmn-font registry, including `bpmn-icon-user-task`, `bpmn-icon-manual-task`, `bpmn-icon-service-task`, `bpmn-icon-script-task`, `bpmn-icon-business-rule-task`, `bpmn-icon-send-task`, `bpmn-icon-receive-task`, `bpmn-icon-call-activity`, and `bpmn-icon-task-none`. The hand-authored task glyph paths below are legacy visual guidance only and must not be emitted by the renderer; the semantic Task subtype remains authoritative.

Render Task type symbols in the top-left corner of the rounded Activity rectangle. Use a normalized `18×18` glyph box placed at `translate(activityX + 8, activityY + 8)`; keep the glyph visually separate from labels and bottom-center Activity markers. Generic `Task` has **no** top-left type symbol.

```xml
<!-- Common Task base shape (100×80 default) -->
<rect class="bpmn-task-shape"
      x="0" y="0" width="100" height="80" rx="10" ry="10"
      fill="#fff" stroke="#000" stroke-width="1.5"/>

<!-- User Task: person icon -->
<g class="bpmn-task-type-user" transform="translate(8 8)">
  <circle cx="9" cy="4.5" r="3" fill="none" stroke="currentColor" stroke-width="1.3"/>
  <path d="M3.5 16 C4.2 11.5, 6.4 9, 9 9 C11.6 9, 13.8 11.5, 14.5 16"
        fill="none" stroke="currentColor" stroke-width="1.3"
        stroke-linecap="round"/>
</g>

<!-- Manual Task: hand icon -->
<g class="bpmn-task-type-manual" transform="translate(8 8)">
  <path d="M4 10 V5.5 C4 4.7, 4.6 4.1, 5.4 4.1 C6.1 4.1, 6.7 4.7, 6.7 5.5 V9
           M6.7 9 V4 C6.7 3.2, 7.3 2.6, 8.1 2.6 C8.9 2.6, 9.5 3.2, 9.5 4 V9
           M9.5 9 V5 C9.5 4.2, 10.1 3.6, 10.9 3.6 C11.7 3.6, 12.3 4.2, 12.3 5 V10
           M12.3 10 L13.3 8.8 C13.9 8.1, 14.8 8.1, 15.3 8.6 C15.8 9.1, 15.8 9.9, 15.2 10.6
           L12.2 14.2 C11.1 15.5, 9.5 16, 7.7 16 H6.7 C4.9 16, 3.5 14.6, 3.5 12.8 V10"
        fill="none" stroke="currentColor" stroke-width="1.2"
        stroke-linecap="round" stroke-linejoin="round"/>
</g>

<!-- Service Task: gear icon -->
<g class="bpmn-task-type-service" transform="translate(8 8)">
  <circle cx="9" cy="9" r="3" fill="none" stroke="currentColor" stroke-width="1.3"/>
  <path d="M9 1.8 V4 M9 14 V16.2 M1.8 9 H4 M14 9 H16.2
           M3.9 3.9 L5.4 5.4 M12.6 12.6 L14.1 14.1
           M14.1 3.9 L12.6 5.4 M5.4 12.6 L3.9 14.1"
        fill="none" stroke="currentColor" stroke-width="1.3"
        stroke-linecap="round"/>
</g>

<!-- Script Task: document with folded corner and script lines -->
<g class="bpmn-task-type-script" transform="translate(8 8)">
  <path d="M4 1.5 H11 L15 5.5 V16.5 H4 Z M11 1.5 V5.5 H15"
        fill="#fff" stroke="currentColor" stroke-width="1.2"
        stroke-linejoin="round"/>
  <path d="M6 8 H13 M6 10.5 H12 M6 13 H10.5"
        fill="none" stroke="currentColor" stroke-width="1.1"
        stroke-linecap="round"/>
</g>

<!-- Business Rule Task: table/grid icon -->
<g class="bpmn-task-type-business-rule" transform="translate(8 8)">
  <rect x="3" y="3" width="12" height="12" fill="#fff"
        stroke="currentColor" stroke-width="1.2"/>
  <path d="M3 7 H15 M3 11 H15 M7 3 V15 M11 3 V15"
        fill="none" stroke="currentColor" stroke-width="1"/>
</g>

<!-- Send Task: filled envelope -->
<g class="bpmn-task-type-send" transform="translate(8 8)">
  <path d="M2.5 5 H15.5 V13.5 H2.5 Z M2.5 5 L9 10 L15.5 5"
        fill="currentColor" stroke="currentColor" stroke-width="1.2"
        stroke-linejoin="round"/>
  <path d="M2.5 13.5 L7.2 9.2 M15.5 13.5 L10.8 9.2"
        fill="none" stroke="#fff" stroke-width="1"/>
</g>

<!-- Receive Task: open/unfilled envelope -->
<g class="bpmn-task-type-receive" transform="translate(8 8)">
  <path d="M2.5 5 H15.5 V13.5 H2.5 Z M2.5 5 L9 10 L15.5 5
           M2.5 13.5 L7.2 9.2 M15.5 13.5 L10.8 9.2"
        fill="#fff" stroke="currentColor" stroke-width="1.2"
        stroke-linejoin="round"/>
</g>
```

Task type rendering rules:
- Top-left Task symbols are derived from the semantic Task subtype (`UserTask`, `ManualTask`, `ServiceTask`, `ScriptTask`, `BusinessRuleTask`, `SendTask`, `ReceiveTask`) and must not be user-editable decoration.
- Generic Task, Sub-Process, Transaction, Ad-hoc Sub-Process, and Call Activity do not use these Task subtype glyphs unless their semantic type is one of the Task subtypes above.
- Send Task and Receive Task intentionally share envelope geometry but differ by fill: Send is filled/black, Receive is unfilled/white.
- Service Task may use one gear or two small gears; the one-gear glyph above is recommended for compact readability at the default `18×18` size.
- Keep all type glyph strokes non-scaling (`vector-effect="non-scaling-stroke"` if the renderer scales the group) so icons remain legible during zoom and export.
- If a Task is selected, hovered, invalid, or highlighted, apply the state styling to the Activity outline first; do not recolor the type symbol in a way that changes semantic fill meaning (especially Send vs Receive).

---

### 4.5 Label layout and word wrapping

- All visible BPMN labels must support deterministic **word wrapping**: shape names, Pool/Participant and Lane names, connector labels, Data Object/Store labels, Group/category labels, and Text Annotation content.
- Wrap text to the effective label rectangle: explicit `BPMNLabel.Bounds` when present; otherwise the renderer's deterministic default label box for that element kind. Subtract configured horizontal/vertical padding before measuring text.
- Prefer wrapping at Unicode word boundaries. If one token exceeds the available line width, break it at grapheme-cluster boundaries rather than overflowing or truncating it. Preserve explicit newline characters as mandatory line breaks.
- SVG rendering must use explicit line layout (for example one `<tspan>` per line) calculated by the shared label-layout service; do not rely on browser-specific SVG automatic wrapping or `foreignObject`.
- The shared icon manifest contains vendored normalized SVG paths imported from a pinned raw-SVG snapshot of bpmn-font, not CSS classes, font-family references, or runtime package imports. Generation records the upstream repository/ref and source file, source glyph name/codepoint, OFL 1.1 attribution, and a deterministic manifest version; source drift or a missing glyph fails the build rather than silently changing notation. The published component package contains the generated paths and license attribution only; consumers do not need bpmn-io, bpmn-font, a webfont, or a network connection.
- Horizontal alignment (`start`/`middle`/`end`), vertical alignment, line height, font metrics, rotation, and maximum line count are configurable through the label style/theme contract. Pool/Lane header labels wrap in their unrotated local coordinate system and are then rotated with the header, so horizontal and vertical Pool orientations use the same wrapping algorithm.
- Default behavior is to grow an auto-sized/default label box vertically to fit all wrapped lines without changing semantic geometry. If explicit imported `BPMNLabel.Bounds` are fixed and text exceeds their height, the renderer must clip or show a configurable ellipsis only as a presentation choice; the complete semantic text remains intact and available to accessibility APIs/tooltips/property inspection.
- Connector labels wrap within their `BPMNLabel.Bounds` without changing edge waypoints. Shape labels wrap without resizing the BPMN shape unless an explicit auto-size policy is enabled. Text Annotations wrap within their shape bounds and may auto-expand only through the normal explicit/compound resize command.
- Inline editing must use the same font metrics, padding, width, line-breaking rules, and explicit-newline handling as committed SVG rendering, preventing text from reflowing unexpectedly when edit mode closes.
- **Label editing is always inline, never a popup/dialog/modal.** Every label-bearing element (shapes, connectors, Pools/Lanes, data/artifacts, Text Annotations) is edited through one in-place text-editing surface positioned exactly over that label's effective bounds on the canvas — there is no separate floating editor window, browser `prompt()`/`alert()`, modal dialog, or off-canvas text box for committing label text. Any menu/toolbar/context-menu action labeled "Edit Label" (or equivalent) invokes this same in-place editor rather than opening a distinct surface. A read-only property-panel field may additionally *display* the current label text for inspection, but it is never the required or only way to change it, and it never behaves as a blocking modal.
- Word wrapping is presentation/DI behavior only. Export/import must preserve the full semantic string; generated SVG/PNG/print output must reproduce the same calculated lines deterministically.

### 4.6 Connector outline anchors and grid snapping

- Connectors attach at arbitrary points on the valid geometric outline of their source and target shapes. BPMN shapes do not expose mandatory fixed ports or side-midpoint-only anchors.
- When grid snapping is disabled, creation, reconnection, and endpoint-handle dragging project the pointer's paper-coordinate position directly to the nearest valid point on the shape outline.
- When grid snapping is enabled, the editor first snaps the pointer's paper-coordinate position to the configured grid, then projects that snapped position to the nearest valid point on the shape outline. The final attachment must remain exactly on the outline even when the outline itself does not intersect a grid point.
- Grid snapping must not quantize an outline into a finite set of ports, force a particular side, or change the connector's semantic `sourceRef`/`targetRef`. Curves, circles, diamonds, rounded rectangles, and custom registered outlines remain continuously attachable.
- The same grid-snap setting and coordinate transform apply consistently to initial connection creation, reconnection to another shape, and movement of an existing endpoint attachment handle. A modifier-key bypass may temporarily disable snapping during a gesture when enabled by host configuration.
- The committed BPMNDI endpoint waypoint stores the resulting projected outline coordinate. Undo/redo must restore the exact committed coordinate and must not recompute it using a later grid-size or grid-enabled setting.

## 5. Validation Rule Engine (editor enforcement)

Implement as a pure function `validateConnection(source, target, connectorType) -> {valid: bool, errorCode?, message?}` run **on drag-release**, plus a whole-diagram `validateDiagram(model) -> Issue[]` run on save/lint.

### 5.1 Per-connection checks (drag-release, real-time)
```
function validateConnection(source, target, kind):
  if kind == SEQUENCE_FLOW:
    if source.type in {EndEvent} -> reject SF001 "End events cannot have outgoing flow"
    if target.type in {StartEvent} -> reject SF002 "Start events cannot have incoming flow"
    if target.type in {BoundaryEvent} -> reject SF003 "Boundary events cannot have incoming sequence flow"
    if source.containerId != target.containerId -> reject SF004 "Sequence flow cannot cross pool/sub-process boundary"
    if source.type == Gateway and source.gatewayKind in {EventBased, ParallelEventBased}:
        if target.type not in {IntermediateCatchEvent(Message|Timer|Signal|Conditional), ReceiveTask}
            -> reject SF005 "Event-based gateway may only target a catching event or receive task"
    if source.type == Gateway and source.gatewayKind == Parallel and hasConditionExpression(this edge)
        -> reject SF006 "Parallel gateway outgoing flow cannot have a condition"
    if source.type == EventSubProcessBoundary -> reject SF007 "Event sub-processes have no external sequence flow"
    return ok

  if kind == MESSAGE_FLOW:
    if source.poolId == target.poolId -> reject MF001 "Message flow endpoints must be in different pools"
    if source.type == Gateway or target.type == Gateway -> reject MF002 "Gateways cannot be message flow endpoints"
    if source.type not in {Pool, Activity, MessageThrowEvent, MessageEndEvent} -> reject MF003 "Invalid message flow source"
    if target.type not in {Pool, Activity, MessageCatchEvent, MessageStartEvent, MessageBoundaryEvent} -> reject MF004 "Invalid message flow target"
    if target.type == EndEvent -> reject MF005 "End events cannot be a message flow target"
    if source.type in {MessageIntermediateEvent} and source.hasIncomingMessageFlow and kind == outgoing
        -> reject MF006 "A message intermediate event cannot have both an incoming and an outgoing message flow"
    return ok

  if kind == ASSOCIATION or kind == DATA_ASSOCIATION:
    if source.type not in {DataObject, DataStore, TextAnnotation, Group?} and target.type not in {...same...}
        -> reject A001 "Association must connect an Artifact to a Flow Object"
    if source.type == Group or target.type == Group -> reject A002 "Groups cannot be connected"
    if kind == DATA_ASSOCIATION:
      if isAny(source, [DataObjectRef, DataStoreRef]) and isAny(target, [Activity, ThrowEvent]) -> ok as DataInputAssociation
      elif isAny(target, [DataObjectRef, DataStoreRef]) and isAny(source, [Activity, CatchEvent]) -> ok as DataOutputAssociation
      else -> reject DA001 "Invalid data association direction/endpoints"
      if not hasTransformation(edge) and countSources(edge) != 1 -> reject DA002 "Data association without a transformation must have exactly one source"
    return ok
```

### 5.2 Whole-diagram (structural) checks
| Code | Rule |
|---|---|
| D001 | Every Start Event has 0 incoming SF |
| D002 | Every End Event has 0 outgoing SF |
| D003 | Every Boundary Event has 0 incoming SF and is attached to exactly one Activity |
| D004 | Product lint (not a BPMN conformance rule): Exclusive/Inclusive split has exactly one flow flagged `isDefault` OR every non-default flow has a `conditionExpression`; flow names do not count as conditions |
| D005 | Parallel/Complex/Inclusive gateways used as merge only synchronize declared incoming flows (warn if incoming count = 1, i.e. redundant gateway) |
| D006 | Event-Based Gateway: all outgoing targets are catch-events/receive-tasks; none carry conditions |
| D007 | No Sequence Flow edge crosses a Pool boundary (graph partition check per pool) |
| D008 | Every Message Flow crosses exactly one Pool boundary |
| D009 | Every node is reachable from some Start Event (no orphan nodes) and every Start Event can reach some End Event (no dead ends) — warning-level |
| D010 | Event Sub-Process start event is typed (not None) and has no drawn SF in/out |
| D011 | Link Intermediate throw/catch names are paired 1:1 within the same Process (multiple sources allowed, only one target) |
| D012 | Compensation boundary event's association target is itself not connected by ordinary SF into the main flow |
| D013 | End Event with ≥1 outgoing Message Flow has Result = Message or Multiple; with >1 outgoing Message Flow has Result = Multiple |
| D014 | Event-Based Gateway outgoing targets are homogeneous (all Message Catch Events, or all Receive Tasks — never mixed) and none of those targets has an additional incoming Sequence Flow |
| D015 | Every `BPMNEdge` has ≥2 waypoints; every `BPMNShape` has positive-size `Bounds`; every DI element's `bpmnElement` resolves to an existing semantic element (drop/flag orphaned DI otherwise) |
| D016 | Complex Gateway with >1 incoming Sequence Flow declares an `activationCondition` (unless profile explicitly allows implicit behavior) — warning-level |
| D017 | Every required reference resolves to an object of the permitted BPMN type: `sourceRef`/`targetRef`, `processRef`, `calledElementRef`, `messageRef`, event-definition references, lane flow-node references, data item references, and compensation attachments |
| D018 | Every semantic object has a unique XML-safe ID within `Definitions`; every DI ID and extension ID is also unique in the serialized document |
| D019 | Every FlowNode belongs to exactly one Process/embedded flow-elements container; every Lane reference is consistent with its containing LaneSet and FlowNode lane membership |
| D020 | Event definitions are legal for their context and catch/throw direction; boundary events have exactly one valid Activity attachment; Event Sub-Process starts have exactly one typed start |
| D021 | Implemented subset: `Definitions.targetNamespace` is non-empty and retained Call Activity / expression QNames have valid, resolved namespace identity; this does not validate every namespace binding or extension QName |
| D022 | Every `BPMNPlane` has a valid diagram owner, every shown semantic element has at most one authoritative DI representation per diagram, and every label/style reference resolves |
| D023 | Global definitions referenced by Call Activities and event/data/message definitions exist, have the expected type, and remain available after import/export |

Return severity: `error` blocks save/export; `warning` is advisory (e.g., D005, D009, D016).

### 5.3 Formal rule-ID catalog (Sequence/Message/Association)

Stable IDs for UI tooltips, automated tests, and documentation cross-references. `SF`=Sequence Flow, `MF`=Message Flow, `DA`=Data Association.

| ID | Rule | Severity |
|---|---|---|
| SF-001 | Source and target must be FlowNodes: Activity, Event, Gateway (never Artifact/Pool/Lane/Data Object). | Error |
| SF-002 | Source must not be an End Event. | Error |
| SF-003 | Target must not be a Start Event. | Error |
| SF-004 | Target must not be a Boundary Event. | Error |
| SF-005 | Source/target must not be an Event Sub-Process (or its shape) — it has no external Sequence Flow. | Error |
| SF-006 | Sequence Flow must not cross a Pool/Participant boundary (`poolId` mismatch). | Error |
| SF-007 | Sequence Flow must not cross a Sub-Process containment boundary (`containerId` mismatch). | Error |
| SF-008 | Source must not be a Compensation Boundary Event (use Association to the handler instead). | Error |
| SF-009 | Source/target must not be a compensation Activity (`isForCompensation=true`) reached via normal flow. | Error |
| SF-010 | Throwing Link Event must have 0 outgoing SF; catching Link Event must have 0 incoming SF; a Link must not be both source and target of the same pair. | Error |
| SF-011 | Event-Based Gateway outgoing target must be a valid Intermediate Catch Event or Receive Task per §2.6 configuration rules. | Error |
| SF-012 | Event-Based Gateway outgoing Sequence Flow must not carry a condition expression. | Error |
| SF-013 | Parallel Gateway outgoing Sequence Flows must not carry conditions or a `default` flag. | Error |
| SF-014 | `isDefault` may only be set on a flow sourced from an Activity, Exclusive, Inclusive, or Complex Gateway. | Error |
| SF-015 | Conditional Sequence Flow from an Activity requires at least one other outgoing Sequence Flow from that Activity. | Error |
| SF-016 | Conditional Sequence Flow must not be sourced from a Parallel or Event-Based Gateway. | Error |
| MF-001 | Source and target must be `InteractionNode`s: Participant/Pool, Activity, or Event. | Error |
| MF-002 | Source and target must be in different Participants/Pools (`poolId` must differ). | Error |
| MF-003 | Gateways are never Message Flow endpoints. | Error |
| MF-004 | Lanes, Data Objects, Groups, and Text Annotations are never Message Flow endpoints. | Error |
| MF-005 | Source must not be a (non-message-throwing) Boundary Event; boundary Message Events catch, not throw. | Error |
| MF-006 | Target must not be an End Event. | Error |
| MF-007 | Source Event must be message-throwing (Message Intermediate Throw, Message End, or Multiple with a message result). | Error |
| MF-008 | Target Event must be message-catching (Message Start, Message Intermediate Catch, Message Boundary, or Multiple with a message trigger). | Error |
| MF-009 | A Message Intermediate Event may have one incoming **or** one outgoing Message Flow, never both. | Error |
| MF-010 | An End Event with ≥1 outgoing Message Flow must have Result = Message/Multiple; with >1 outgoing Message Flow, Result must be Multiple. | Error |
| DA-001 | Association must never be treated as token/control flow. | Error |
| DA-002 | Group cannot be a connector endpoint (visual grouping only). | Error |
| DA-003 | Text Annotation connects only via (undirected) Association, never Sequence/Message Flow. | Error |
| DA-004 | Data Object/Data Store require Data Association except when linked to a Text Annotation. | Error |
| A-002 | A flow-line Association endpoint requires a Text Annotation and a Sequence Flow or Message Flow. | Error |
| DA-005 | Data Input Association direction is Data Object/Store/Property → Activity or Throwing Event input. | Error |
| DA-006 | Data Output Association direction is Activity or Catching Event output → Data Object/Store/Property. | Error |
| DA-007 | DataAssociation without a transformation expression must have exactly one source. | Error |
| DA-008 | DataAssociation with mismatched item definitions on source/target requires a transformation expression. | Error |
| DA-009 | Compensation Association source must be a Compensation Boundary Event; target must be an Activity in the same scope, not the host Activity, and not an Event Sub-Process. | Error |
| CT-001 | A rendered cell's JointJS parent does not match the nearest visual container derived from semantic ownership, Lane membership, attachment, or auxiliary ownership. | Error |
| CT-002 | JointJS embedding has multiple direct parents or contains a parent/descendant cycle. | Error |
| CT-003 | A BPMN Group or transient selection wrapper is used as a JointJS containment parent. | Error |
| CT-004 | Lane embedding and `flowNodeRef` membership disagree, or a FlowNode belongs to more than one incompatible leaf Lane. | Error |
| CT-005 | A Boundary Event is not embedded in the Activity referenced by `attachedToRef`, or is embedded in a different Activity. | Error |
| CT-006 | A scoped link is embedded outside its derived lowest common container, or a cross-Pool Message Flow is embedded inside a Pool. | Error |
| CT-007 | A collapsed Sub-Process or Call Activity has visible embedded process-content cells in the active plane. | Error |
| CT-008 | A containment/reparent command left semantic ownership, membership, BPMNDI, link hierarchy, z-order, or JointJS embedding partially updated. | Error |
| CT-009 | A Sub-Process expansion state disagrees with `BPMNShape.isExpanded`, or a collapsed Sub-Process exposes active nested visual cells/tools. | Error |
| CT-012 | A Lane's `LaneSet` is on a Process that no `Participant` references, so the Lane subdivides nothing. | Error |

### 5.4 Rule-code range table (namespacing for future rules)

| Code range | Meaning |
|---|---|
| SF-001..099 | Sequence Flow endpoint/scope/marker rules. |
| MF-001..099 | Message Flow endpoint/pool/event rules. |
| DA-001..099 | Association and Data Association rules. |
| EV-001..199 | Event trigger/context/fill/border rules. |
| GW-001..099 | Gateway condition/default/configuration rules. |
| CT-001..099 | Containment, Pool/Lane/Sub-Process rules. |
| DI-001..099 | BPMNDI bounds/waypoints/reference/orientation rules. |
| XML-001..099 | XML schema, namespaces, serialization rules. |
| PROF-001..199 | Optional execution-engine profile rules (out of BPMN-core scope). |

### 5.5 Compliance validator pipeline

Run validators in this order and retain all issues rather than stopping after the first failure:

1. **XML and namespace layer**: parse safely, reject malformed XML, establish namespace bindings, validate XML-safe IDs, and quarantine unsafe external content.
2. **Schema layer**: validate BPMN20, Semantic, BPMNDI, DI, and DC structure/types where schemas are available.
3. **Reference/index layer**: build the global ID index and resolve every core, DI, extension, and declared profile reference.
4. **Containment layer**: validate Process/Sub-Process/Lane/Participant ownership and graph-scope boundaries.
5. **Notation/connection layer**: validate endpoint compatibility, event context, gateways, markers, and pool/lane rules.
6. **Document semantic layer**: validate defaults, conditions, event pairings, data mappings, global definitions, and required attributes.
7. **Extension/profile layer**: run registered adapters and optional execution-engine profiles without weakening core errors.
8. **DI/renderability layer**: validate bounds, waypoints, labels, styles, expanded/collapsed state, orientation, and deterministic SVG render inputs.

The editor must support three outcomes: renderable and exportable, renderable with warnings, or quarantined/non-exportable. A malformed or semantically unsafe fragment must not be silently converted into a valid-looking BPMN element.

---

## 6. Editor Data Model (suggested JSON Schema fragment)

```json
{
  "definitions": {
    "id": "string",
    "targetNamespace": "uri",
    "namespaceBindings": { "prefix": "uri" },
    "rootElementIds": ["id"],
    "diagramIds": ["id"],
    "extensionDefinitionIds": ["id"]
  },
  "process": {
    "id": "string",
    "name": "string|null",
    "isExecutable": "boolean|null",
    "isClosed": "boolean|null",
    "flowElementIds": ["id"],
    "laneSetIds": ["id"],
    "artifactIds": ["id"]
  },
  "globalProcess": {
    "id": "string",
    "flowElementIds": ["id"],
    "global": true
  },
  "globalTask": {
    "id": "string",
    "type": "globalTask|globalUserTask|globalServiceTask|globalScriptTask|globalManualTask|globalBusinessRuleTask",
    "resourceRoleIds": ["id"],
    "ioSpecificationId": "id|null"
  },
  "messageDefinition": {
    "id": "string", "itemRef": "id|null", "name": "string|null"
  },
  "eventDefinitionReference": {
    "type": "message|timer|error|escalation|cancel|compensation|conditional|link|signal|multiple|parallelMultiple|terminate",
    "definitionId": "id|null",
    "eventDefinitionIds": ["id"]
  },
  "flowNode": {
    "id": "string",
    "type": "startEvent|endEvent|intermediateCatchEvent|intermediateThrowEvent|boundaryEvent|task|userTask|serviceTask|scriptTask|manualTask|businessRuleTask|sendTask|receiveTask|subProcess|eventSubProcess|transaction|adHocSubProcess|callActivity|exclusiveGateway|inclusiveGateway|parallelGateway|complexGateway|eventBasedGateway",
    "eventDefinition": "none|message|timer|error|escalation|cancel|compensation|conditional|link|signal|multiple|parallelMultiple|terminate",
    "isInterrupting": true,
    "attachedToRef": "id-of-host-activity (boundary events only)",
    "containerId": "id-of-process-or-subprocess",
    "poolId": "id-of-participant",
    "isForCompensation": false,
    "loopCharacteristics": "none|standard|multiInstanceSequential|multiInstanceParallel",
    "instantiate": false,
    "calledElementRef": "id-of-global-Process-or-Task (callActivity only; mutually exclusive with flowElementsOwned)",
    "flowElementsOwned": "bool — true for embedded subProcess/eventSubProcess/transaction/adHocSubProcess (has its own nested flowElements container); always false for callActivity",
    "extensionElements": [{ "namespace": "uri", "localName": "string", "attributes": {}, "children": [] }]
  },
  "sequenceFlow": {
    "id": "string", "sourceRef": "id", "targetRef": "id",
    "conditionExpression": "string|null", "isDefault": false
  },
  "messageFlow": { "id": "string", "sourceRef": "id", "targetRef": "id", "messageRef": "id|null" },
  "association": { "id": "string", "sourceRef": "id", "targetRef": "id", "direction": "none|one|both" },
  "dataAssociation": {
    "id": "string", "kind": "input|output",
    "sourceRefs": ["id"], "targetRef": "id",
    "transformation": "string|null"
  },
  "participant": { "id": "string", "name": "string", "processRef": "id|null" },
  "laneSet": { "id": "string", "lanes": ["id"] },
  "lane": { "id": "string", "name": "string", "flowNodeRefs": ["id"], "childLaneSetId": "id|null" },
  "bpmnShapePool": {
    "id": "string", "bpmnElement": "id (Participant)",
    "bounds": { "x": "number", "y": "number", "width": "number", "height": "number" },
    "isHorizontal": true,
    "isExpanded": true
  },
  "bpmnShapeLane": {
    "id": "string", "bpmnElement": "id (Lane)",
    "bounds": { "x": "number", "y": "number", "width": "number", "height": "number" },
    "isHorizontal": "matches-parent-pool"
  },
  "bpmnLabel": {
    "id": "string|null",
    "text": "string",
    "bounds": { "x": "number", "y": "number", "width": "number", "height": "number" },
    "styleId": "id|null"
  },
  "bpmnLabelStyle": {
    "id": "string",
    "fontFamily": "string",
    "fontSize": "number",
    "fontWeight": "normal|bold",
    "fontStyle": "normal|italic",
    "textDecoration": "none|underline|lineThrough"
  },
  "iconManifest": {
    "sourceRepository": "https://github.com/bpmn-io/bpmn-font",
    "sourceRef": "pinned git commit or vendored raw-SVG snapshot",
    "sourceFile": "dist/font/bpmn.svg",
    "license": "OFL-1.1",
    "icons": [{
      "iconId": "string",
      "upstreamName": "bpmn-icon-*",
      "codePoint": "string",
      "semanticContext": "string",
      "pathData": "string",
      "viewBox": "string",
      "baseline": "number",
      "alignment": "start|middle|end",
      "accessibilityLabel": "string"
    }]
  },
  "bpmnPlane": {
    "id": "string",
    "bpmnElement": "id (Process|Collaboration|SubProcess|null)",
    "shapeIds": ["id"],
    "edgeIds": ["id"]
  },
  "bpmnDiagram": {
    "id": "string",
    "planeId": "id",
    "name": "string|null"
  },
  "extensionDefinition": {
    "id": "string",
    "name": "string",
    "namespace": "uri",
    "referencedAttributes": ["uri#localName"]
  },
  "extensionAttributeValue": {
    "namespace": "uri",
    "localName": "string",
    "value": "string|number|boolean|null",
    "rawXml": "string|null"
  }
}
```

Notes:
- `isHorizontal` is defined only on Pool/Lane `BPMNShape` entries; it does not exist on Event/Activity/Gateway/Data-Object shapes.
- `dataAssociation.sourceRefs` is an array to accommodate the "no transformation ⇒ exactly one source" vs "with transformation ⇒ zero-to-many sources" rule from §2.9.
- `calledElementRef` and `flowElementsOwned` are mutually exclusive by construction (§1.2.1/§2.7): a `callActivity` always has `flowElementsOwned=false` and a non-null `calledElementRef`; every embedded Sub-Process kind (`subProcess`, `eventSubProcess`, `transaction`, `adHocSubProcess`) always has `flowElementsOwned=true` and `calledElementRef=null`. The editor should reject any model state that violates this pairing.

### 6.1 Extensibility model

BPMN 2.0 is intentionally extensible. Extensibility must be implemented as a first-class model layer rather than by dropping unknown XML or attaching arbitrary properties to SVG nodes.

#### 6.1.1 Supported BPMN extension points

| Extension point | Purpose | Editor treatment |
|---|---|---|
| `extensionElements` on `BaseElement` descendants | Container for vendor-defined XML elements associated with a BPMN element (for example, execution-engine configuration, forms, listeners, task metadata, or custom documentation). | Preserve ordered child elements, namespace declarations, attributes, text, and nested children. Expose known extensions through adapters; retain unknown extensions as opaque XML. |
| `extensionDefinitions` on `Definitions` | Declares a named extension and its namespace, optionally describing referenced extension attributes. | Parse, index, validate namespace identity, and round-trip. Do not silently remove a definition because the editor does not understand its payload. |
| `extensionAttributeDefinition` / extension attribute values | Describes or carries custom attributes in an extension namespace. | Preserve namespace-qualified names and lexical values; validate them only when a registered extension adapter is available. |
| XML namespace declarations, imports, and vendor namespaces | Identify the vocabulary and schema for custom elements/attributes. | Preserve prefix-to-URI mappings on import/export; never treat a prefix itself as the identity. URI plus local name is authoritative. |
| BPMNDI extension content | Allows tool-specific DI data such as routing hints, bend-point metadata, rendering preferences, or layout locks. | Keep DI extensions separate from semantic extensions and preserve them even when the renderer ignores them. |

#### 6.1.2 Namespace and identity rules

- Every extension element or attribute must have a stable namespace URI and local name. Store the expanded name `{namespaceURI}localName`; prefixes are serialization aliases only.
- Do not use unqualified custom attributes on BPMN elements. Custom attributes must be namespace-qualified so they cannot collide with future BPMN attributes or another vendor's vocabulary.
- Use the document's declared namespace bindings when exporting, but permit prefix normalization. A round trip must preserve the namespace URI and local name even if the chosen prefix changes.
- Namespace URIs are case-sensitive and must be compared exactly. Do not infer vendor identity from a prefix, schema filename, or JavaScript/C# type name.
- Maintain a namespace registry with URI, preferred prefix, schema URL (if known), version, and an optional extension adapter identifier.
- Reject duplicate namespace registrations with incompatible metadata; allow multiple prefixes that resolve to the same URI.
- Extension IDs remain subject to the same global ID uniqueness and XML identifier rules as core BPMN IDs. Do not use an extension's local name as an ID.

#### 6.1.3 Import, edit, and export behavior

1. Parse core BPMN semantics and BPMNDI first; parse extension payloads into an ordered extension tree without trying to coerce unknown XML into core BPMN types.
2. Record the owning semantic or DI element for every extension node. An extension must not be detached from its owner when the element is moved, copied, collapsed, expanded, or reparented.
3. Preserve unknown extension XML losslessly where possible: element order, namespace-qualified names, attributes, text, child order, and raw lexical values (including values that look like IDs or expressions).
4. On copy/paste, deep-clone extension content with the owning element. Rewrite references only when the extension adapter explicitly declares a reference attribute; never blindly rewrite arbitrary strings.
5. On delete, remove extensions owned by the deleted element, but do not delete a namespace registration or global extension definition while another element or document-level definition still uses it.
6. On export, serialize core BPMN and DI first, then emit extension content at the correct owner location. Preserve semantic `extensionElements` separately from DI extension content.
7. If an extension adapter changes a known payload, mark the document dirty and provide an undoable model command. Do not silently normalize or rewrite an extension on load.
8. If an extension cannot be parsed safely, preserve its raw XML and report an import warning; do not fabricate a successful core BPMN interpretation.

#### 6.1.4 Extension adapters and profiles

An extension adapter is an optional module registered by namespace URI. It may provide:

- A typed view/model for known extension XML.
- XML parse/serialize functions.
- Attribute and child-element validation.
- Property-panel editors and commands.
- SVG decorations or overlays.
- Connection, containment, stencil, and save-time validation rules.
- Declared reference attributes that may be safely remapped during copy/paste or ID regeneration.

Adapters must be isolated from BPMN-core rules:

- Core BPMN validation always runs, even when no adapter is installed.
- An unknown extension must not make a valid core BPMN graph invalid merely because the editor cannot interpret the extension.
- An adapter may add errors/warnings for its own namespace, but must not silently weaken a BPMN-core error.
- Execution-engine profiles (for example, vendor-specific executable attributes) belong in adapters/profile validators, not in the normative BPMN connection engine.
- Version an adapter by namespace URI plus extension version; do not assume all payloads in one namespace share one schema version.

Implementation note: the library maps this adapter concept to opt-in `BpmnExtensionOptions`
schema registration, snapshot `IBpmnExtensionGroupBuilder`s / `IBpmnPropertiesProvider`s,
undoable binding commands, and `IBpmnProfileValidator`s. Element templates compose these
facilities rather than replace BPMN-core rules. See [Extensions & element templates](../src/Bpmn/README.md#extensions--element-templates)
for registration and host customization.

#### 6.1.5 Rendering extensions in SVG

- Core BPMN notation remains the primary rendering layer. Extension decorations are secondary overlays and must never replace or obscure required BPMN markers.
- Unknown extensions are not rendered as invented BPMN symbols. Show an optional neutral extension badge/outline and an accessible tooltip indicating the namespace and local name.
- Known extension SVG decorations must use a separate SVG group (for example, `data-layer="extension"`) and stable z-order: core shape → core markers → labels → extension overlay → selection/validation UI.
- Extension overlays must be toggleable, must not become connector endpoints unless the adapter explicitly registers that behavior, and must not alter semantic `sourceRef`/`targetRef`.
- Extension rendering must remain deterministic and must degrade gracefully when the adapter is unavailable.
- Accessibility output must expose extension metadata through `<title>`/`<desc>` or an equivalent inspector, without claiming that a vendor extension is standard BPMN.

#### 6.1.6 Extension validation and security

- Validate extension XML against a registered schema only when the schema is available and trusted; schema validation is supplementary to the core semantic validator.
- Unknown extension content should produce an `info`/`warning`, not a hard error, unless the selected execution profile requires that extension.
- Never execute scripts, expressions, external entity references, embedded URLs, or vendor payloads while parsing or rendering. Treat extension text as data.
- Disable external entity resolution and restrict external schema/resource fetching by default. Keep schema fetching explicit, bounded, and auditable.
- Sanitize extension-provided SVG fragments. Prefer adapter-generated paths over injecting raw SVG/XML into the document.
- Do not log extension payloads by default because they may contain credentials, personally identifiable information, or proprietary configuration.

#### 6.1.7 Extension rule IDs

Use stable codes for extension diagnostics:

| Code | Meaning |
|---|---|
| EXT-001 | Extension element/attribute has no namespace URI. |
| EXT-002 | Namespace declaration or prefix mapping is inconsistent. |
| EXT-003 | Extension XML is malformed or cannot be preserved safely. |
| EXT-004 | Registered adapter rejected the extension payload. |
| EXT-005 | Extension reference could not be resolved. |
| EXT-006 | Extension attempted to override a BPMN-core rule or marker. |
| EXT-007 | Extension SVG decoration was suppressed because the adapter is unavailable or unsafe. |
| EXT-008 | Extension schema/resource access was blocked by the security policy. |

---

## 7. Editor UX Enforcement Modes
- **Strict mode (default)**: blocks any drag-release that fails §5.1 with a red halo + tooltip error code/message; disallows starting a connector drag from an End Event or a Boundary Event's incoming side.
- **Permissive mode**: allows technically-legal-but-discouraged constructs (mixed gateway fan-in/out, multi-incoming End Events) but surfaces them as D005/D009/D016-style lint warnings in a side panel.
- **Canvas context-menu exclusivity:** while the pointer/focus target is anywhere inside the editor canvas root—including blank paper, shapes, connectors, labels, selection/resize/link tools, and HTML editing overlays—the browser's native context menu must never be reachable. A capture-phase `contextmenu` handler on the canvas root always calls `preventDefault()` and routes the request to the Blazor-owned custom context menu in both Edit and Readonly modes. This suppression must not affect DOM outside the editor canvas root.
- Right-click, the keyboard Context Menu key/`Shift+F10`, and a configured touch long-press all invoke the same custom-menu request path. Shape/link targets use their paper-relative bounds; blank-canvas requests use a zero-size paper-relative anchor at the invocation point; overlay/tool targets resolve to their owning BPMN/DI object or the canvas. If no target-specific commands are available, the Blazor surface still opens the canvas-level custom menu with applicable inspection/navigation/export commands (or a disabled empty-state item); it never shows the browser menu and never silently falls back to native behavior.
- **Context-menu element data and editing:** when the resolved target owns a BPMN element, `contextMenuRequested` must include a versioned, typed property snapshot containing the element ID/type/category, semantic owner/container and Pool/Lane scope, label, standard BPMN attributes and event definitions, relevant references, current DI bounds/label bounds, capability flags, validation diagnostics, and the list of editable standard properties. The snapshot is sufficient for `BpmnContextMenu` to display and bind standard element properties without re-querying or inferring them from SVG/JointJS cells; it must not include stale or extension-only values as standard fields. Property edits are typed, validated, and submitted as commands through the normal Blazor→TypeScript path, with an atomic result and updated snapshot/diagnostics. In Edit mode the menu may expose a **Switch type** operation, limited to registered BPMN types in the same element category and valid context: for example, Service Task ↔ Message Task and None Start Event ↔ Message Start Event are eligible, while Task ↔ Gateway, Start Event ↔ End Event, or any Choreography/Conversation type is not. A switch preserves the element ID, position/size where valid, label, compatible connections, containment, references, and DI; incompatible properties are cleared or reported explicitly, and the whole conversion is rejected atomically if the resulting element or connections would violate BPMN rules. Readonly mode exposes the snapshot and inspection controls only; property binding and type switching are disabled.
- Stencil should visually grey out incompatible target shapes while a connector is being dragged, computed live via `validateConnection` against the candidate source.
- **Quick fixes** (offered alongside error tooltips, not auto-applied): replace an invalid cross-pool Sequence Flow with a Message Flow when endpoints are compatible; insert a Gateway before an Intermediate/End Event with multiple uncontrolled incoming flows; split a gateway with both multiple incoming and multiple outgoing flows into a merge gateway followed by a split gateway; add a missing `isDefault` flow to an Exclusive/Inclusive/Complex split; strip an accidental condition expression from a flow marked default; convert a normal-flow Error/Cancel Intermediate Event into an equivalent Boundary Event on the nearest enclosing Activity.

---

## 8. Explicit Exclusions
- Choreography Task, Choreography Sub-Process, Sub-Choreography, Call Choreography — **excluded**.
- Conversation Diagram, Conversation Link, Conversation Node — **excluded** (Choreography-dependent).
- On import, if any excluded element type is encountered in foreign BPMN XML, preserve it losslessly in the underlying model (do not delete/corrupt the file) but do not render or make it editable; surface a non-blocking "unsupported element" import warning per occurrence.

---

## 9. Stencil & Creation Rules

### 9.1 Stencil by container

| Container | Allowed new elements |
|---|---|
| Empty canvas (no root Process/Collaboration yet) | Create a `Collaboration` document root with a new Pool/Participant, or create a standalone `Process`; Text Annotation and Group may be created only after a BPMNDiagram/BPMNPlane exists. Do not create FlowNodes directly on the Definitions/canvas root. |
| Collaboration plane (root `Collaboration` already exists) | Pool/Participant, Text Annotation, Group, and Message Flow between compatible Participants/Pool boundaries or InteractionNodes in different Pools. Do not create Sequence Flow, FlowNodes, Lanes, or Data Objects directly as Collaboration children; create them inside a Participant's referenced Process. |
| Standalone Process plane (root `Process` with no Participant) | Start/Intermediate/End Events, Activities, Gateways, Data Object/Data Store References, Text Annotation, Group, and Sequence Flow. **Not Lanes** — a Lane subdivides a Pool (§1.5) and there is none here; the tool is shown disabled with `CT-012` rather than hidden. The Process remains a root element in `Definitions`; do not create a Pool implicitly. |
| Pool with referenced Process | Start/Intermediate/End Events, Activities, Gateways, Data Object/Data Store References, Text Annotation, Group, optional Lanes, Sequence Flow, and Boundary Events attached to Activities. |
| Lane | Same FlowNodes and artifacts as the parent Process/Pool, assigned through Lane membership and a containing `LaneSet`; a Lane is organizational metadata, not a separate FlowElementsContainer or Sequence Flow scope. |
| Expanded embedded Sub-Process | Start/Intermediate/End Events, Activities, Gateways, Data Object/Data Store References, Text Annotation, Group, Sequence Flow, and Boundary Events on nested Activities (all scoped to that Sub-Process's own `containerId`). |
| Event Sub-Process | Exactly one typed (non-None) Start Event and its internal Events/Activities/Gateways/References/Sequence Flows; no externally-drawn Sequence Flow in or out and no ordinary external entry point. |
| Transaction Sub-Process | Normal embedded Sub-Process content plus Cancel End Event and Cancel Boundary Event handling; expose Compensation only where its transaction/handler semantics are valid. |
| Call Activity | An opaque Activity in the current Process with `calledElementRef` pointing to an existing or newly-created reusable global Process/Global Task. Never create local FlowNodes or a nested LaneSet inside it. |
| Global definition/reference manager | Create or edit reusable global Process/Global Task definitions, Message/Signal/Error/Escalation/ItemDefinition definitions, Interfaces/Operations, Resources, Properties, and Correlation definitions. These are semantic model objects and are not placed directly on a diagram plane unless a referencing shape is created. |
| Black-box Pool (no Process assigned) | Message Flow endpoint only; no internal FlowNodes creatable until a Process is assigned/expanded. |

Creation invariants for the empty canvas and Collaboration plane:
- The first Pool creation must create a `Participant` semantic object and its `BPMNShape`; it must not create a Lane or a Process implicitly unless the user selects an “add Process”/“expand Pool” action.
- A process-backed Pool is also valid with zero Lanes because `Process.laneSet` has schema cardinality `0..*`. Adding a Process must not implicitly add a Lane, and deleting the final Lane must leave a valid lane-less Process/Pool.
- A Participant may remain black-box (`processRef=null`). A black-box Pool is still a valid Message Flow endpoint, but it has no FlowNode, Lane, Sequence Flow, Data Association, or internal DI content.
- Assigning a Process to a Participant (`processRef`) changes the available stencil to the Pool-with-Process row; it does not move the Process's FlowNodes into the Collaboration scope. The Process remains the Participant's referenced semantic root.
- A Participant may reference only one Process through `processRef`, while one Process may be referenced by more than one Participant only when the selected modeling/profile rules permit it; the editor must surface that sharing explicitly rather than silently cloning the Process.
- Creating a second Pool in an existing Collaboration creates another Participant in the same Collaboration. Message Flow becomes available only after endpoint and different-Pool validation succeeds; a Pool-to-itself Message Flow is never allowed.
- Text Annotations and Groups created on a Collaboration plane belong to the Collaboration's artifact collection and are visual/document annotations; they are not FlowNodes and cannot become Sequence Flow or Message Flow endpoints.
- Creating a Data Object or Data Store on a Process/Pool plane creates a visual `DataObjectReference`/`DataStoreReference` linked to a semantic `DataObject`/`DataStore` definition; the editor must offer “new definition” versus “reference existing definition” and must not duplicate definitions accidentally.
- Creating a Call Activity must prompt for an existing reusable global Process/Global Task or offer an explicit “create global definition” action; it must set `calledElementRef` and must never create a hidden embedded graph.
- Creating a Lane creates or selects a `LaneSet`, assigns `flowNodeRefs`/lane membership without changing `containerId`, and synchronizes Lane/Pool orientation (`isHorizontal`).
- If the user chooses “New Process” on an empty canvas instead of “New Collaboration,” create a Process root and its BPMNPlane without a Participant/Pool. A later Collaboration action may reference that Process from a Participant through `processRef`; do not duplicate or re-parent its FlowNodes silently.

### 9.2 Excluded stencil elements

Never expose these in the stencil (consistent with §8 Explicit Exclusions):

- Choreography Task, Sub-Choreography, Call Choreography, Choreography participant bands.
- Conversation, Sub-Conversation, Call Conversation, Conversation Link, Conversation Association.

If imported BPMN XML contains these elements: preserve them losslessly in the underlying model (round-trip only, never delete/corrupt), do not render or make them editable, and surface a non-blocking "unsupported element" import warning per occurrence — matching §8's import-preservation note.

### 9.3 Creation transaction and stencil enforcement

Every stencil action is an atomic model command with undo/redo support:

1. Resolve the active diagram/plane and semantic owner (`Definitions`, `Process`, `Collaboration`, `Participant`, LaneSet, or embedded Sub-Process).
2. Generate fresh globally unique XML-safe IDs for the new semantic object and all required child/reference objects.
3. Validate the proposed type, container, event-definition context, `processRef`/`calledElementRef`, lane membership, and extension/profile requirements before committing.
4. Create the semantic object(s), required DI shape/edge/label entries, and owner references in one transaction.
5. Rebuild indexes and run incremental validation; if any hard error is introduced, roll back the complete command rather than leaving a partial object.

Creation-specific invariants:
- Every created drawable element gets exactly one `BPMNShape` or `BPMNEdge` in the active diagram unless the element is intentionally semantic-only or hidden.
- Every created FlowNode is assigned to exactly one Process/embedded flow-elements container before it can participate in Sequence Flow.
- A created non-boundary Event, Activity, or Gateway is committed only when its effective geometry does not collide with another non-boundary Event, Activity, or Gateway at the same containment level in the active plane; an invalid stencil drop is rejected with `DI-011` and leaves no partial semantic or DI object.
- Every created connector is created only after endpoint validation; incomplete connector gestures are transient UI state and must not be serialized.
- Event definitions are selected only from the valid context matrix in §1.1. Changing an Event's context (Start, Intermediate Catch/Throw, Boundary, End, or Event Sub-Process Start) must revalidate or clear incompatible definitions.
- Boundary Event creation requires an Activity host and immediately sets `attachedToRef`; it cannot exist as a free-floating Event.
- Event Sub-Process creation creates its typed Start Event and internal container atomically; it cannot be committed with a None Start or external Sequence Flow.
- Call Activity creation creates or selects a valid `calledElementRef`; embedded Sub-Process creation creates a local `flowElements` container. The two commands must never share an implementation path that could produce both representations.
- Pool creation creates a Participant and Pool shape; Process assignment is a separate command. Lane creation creates/uses a LaneSet and updates membership without changing Process/Sub-Process scope.
- Data Object/Data Store stencil actions distinguish definitions from references. A reference may be placed on multiple diagrams; deleting a reference must not delete its shared definition without explicit confirmation and reference-count validation.
- Unsupported Choreography/Conversation creation commands are absent from the stencil and rejected by the command API even if invoked programmatically.
- Extension-enabled creation must register namespaces and attach extension payloads to their owner in the same transaction; adapter failures roll back the semantic object and its DI.

Stencil state must be recomputed from the active semantic owner and current profile. A disabled tool must include a reason code (for example, `CT-001`, `EV-001`, or `EXT-004`) rather than merely hiding a failure.

---

## 10. BPMNDI & XML Interchange Validation

The SVG canvas is a **view** generated from two persisted layers: the BPMN semantic model (Process/Collaboration elements and their attributes/references) and the BPMN DI layout model (`BPMNDiagram` → `BPMNPlane` → `BPMNShape`/`BPMNEdge`/`BPMNLabel`). Never treat the SVG as the source of truth.

### 10.1 Reference resolution rules
- Every `BPMNShape`/`BPMNEdge` **must** reference an existing semantic element via `bpmnElement`; if the reference is missing or unresolvable, the shape/edge **must not** be rendered (drop or quarantine it, don't guess).
- A `BPMNEdge`'s visual `sourceElement`/`targetElement` **may** differ from the semantic `SequenceFlow`/`MessageFlow`'s `sourceRef`/`targetRef` — this is the documented mechanism for, e.g., a Message Flow drawn to the boundary of a black-box Pool or a collapsed Sub-Process rather than to the actual inner Flow Node. Resolve visual endpoints from the DI attributes, not from the abstract semantic reference, when both are present.
- A conditional Sequence Flow's mini-diamond marker **must not** be drawn when the flow's source is a Gateway (only when the source is an Activity) — even though the underlying `conditionExpression` is set the same way in both cases.
- Label visibility is controlled purely by the *presence* of a `BPMNLabel` element; label `Bounds` are optional and, when omitted, the renderer must fall back to a deterministic default position for that shape/edge kind.

### 10.2 Geometry integrity
| Check | Rule |
|---|---|
| DI-001 | Every `BPMNEdge` has **≥2** waypoints (schema minimum) |
| DI-002 | Every `BPMNShape` has a `Bounds` with positive `width`/`height` |
| DI-003 | Every `Point`/`Bounds` has finite numeric `x`/`y` (no `NaN`/`Infinity`) |
| DI-004 | `isExpanded` on collapsible shapes (Sub-Process, Transaction, Ad-hoc Sub-Process, Event Sub-Process) is consistent with whether their nested graph is actually rendered |
| DI-005 | `isMarkerVisible` / `isMessageVisible` flags are only set on shapes whose semantic element supports that marker |
| DI-010 | A Boundary Event `BPMNShape.Bounds` center does not lie on its host Activity's (`attachedToRef`) rendered outline (within a small pixel tolerance), or its position was not recomputed after the host's `Bounds` last changed |
| DI-011 | Two non-boundary Event, Activity, or Gateway shapes at the same containment level have intersecting effective geometry after applying the configured interaction clearance |
| DI-012 | A context-menu property edit or same-category type switch would leave an invalid standard BPMN property, reference, connection, containment, DI, or unsupported category conversion |

### 10.3 Export/import pipeline
1. Serialize semantic model → BPMN XML rooted at `Definitions`.
2. Serialize layout model → `BPMNDiagram`/`BPMNPlane` inside the same `Definitions`.
3. Validate the combined document against the official schema chain: `BPMN20.xsd` → (imports) `BPMNDI.xsd` → (imports) `DI.xsd`/`DC.xsd`; `BPMN20.xsd` also includes `Semantic.xsd`.
4. Run the semantic validators from §5 that cannot be expressed in XSD alone (e.g., Event-Based Gateway homogeneity, Cancel-only-in-Transaction, compensation visibility).
5. On import, run the same pipeline in reverse and re-run §5 validators before allowing the diagram to be edited, so foreign/hand-written BPMN files are never silently trusted.

Reference schemas (authoritative structure, not full semantics):
- `BPMN20.xsd` — entry point; declares `Definitions`, imports `BPMNDI.xsd`, includes `Semantic.xsd`.
- `Semantic.xsd` — BPMN model element/attribute/reference definitions (`SequenceFlow`, `MessageFlow`, `Association`, `BoundaryEvent`, gateways, etc.).
- `BPMNDI.xsd` — `BPMNDiagram`, `BPMNPlane`, `BPMNShape`, `BPMNEdge`, `BPMNLabel`, `BPMNLabelStyle`.
- `DI.xsd` — generic `Diagram`/`Plane`/`Shape`/`Edge`/`Label` primitives; `Edge` requires `waypoint` `minOccurs="2"`.
- `DC.xsd` — `Bounds` (`x`, `y`, `width`, `height`, all required `double`) and `Point` (`x`, `y` required `double`).

### 10.4 Pool/Lane orientation (`isHorizontal`)
- `BPMNShape.isHorizontal` (declared in `BPMNDI.xsd`) is the DI attribute that drives the horizontal/vertical rendering described in §1.5. It is meaningful only on Pool and Lane shapes.
- DI-006: every Lane `BPMNShape.isHorizontal` must equal its parent Pool `BPMNShape.isHorizontal` — treat a mismatch as an interchange error (either from a hand-edited file or an editor bug), not a legal mixed layout.
- DI-007: when `isHorizontal=true`, a Pool/Lane's header band occupies the left edge of `Bounds` (a vertical strip); when `isHorizontal=false`, the header band occupies the top edge (a horizontal strip). The renderer must branch on this flag rather than inferring band placement from `Bounds` aspect ratio.
- On import of foreign BPMN files, if `isHorizontal` is absent on a Pool/Lane shape, default to `true` (horizontal) to match the most common tool convention, and normalize it explicitly on next save.
- Rotating orientation (§1.5.3) is a layout-only transform: recompute `Bounds` for the Pool, all Lanes, and all contained shapes/edge waypoints; never touch `sourceRef`/`targetRef`, `containerId`, or `poolId`.

### 10.5 Extensibility interchange rules

- Serialize `extensionDefinitions` and namespace declarations at the `Definitions` level, and serialize each element's `extensionElements` at the owning BPMN element. Do not flatten all extensions into one global bag.
- Preserve extension content through the complete import → edit → export cycle, including unknown vendor XML, namespace-qualified attributes, child order, text content, and DI-specific extension content.
- Validate core BPMN and BPMNDI independently of extension adapters. An unavailable vendor adapter must not cause the editor to delete, reinterpret, or silently rewrite the extension payload.
- Run registered extension schema/semantic validators after core XML schema validation. Extension validation may report `EXT-*` errors/warnings, but an extension cannot suppress a core `SF-*`, `MF-*`, `DA-*`, `EV-*`, `GW-*`, `CT-*`, or `DI-*` error.
- Validate extension reference attributes only when declared by the adapter. Never rewrite arbitrary strings during ID regeneration, copy/paste, or import normalization.
- Ensure every extension namespace used by an element is declared in the exported XML and that all namespace-qualified names resolve to the same URI used by the in-memory model.
- Keep extension XML data separate from SVG. Generate optional safe overlays from typed adapters; never inject raw extension XML or untrusted SVG directly into the SVG DOM.
- If an extension defines custom connection or containment behavior, register it explicitly with the rule engine and identify it as profile/extension behavior. It must not silently broaden BPMN-core endpoint rules.
- When exporting without an adapter, emit the preserved raw extension XML unchanged where possible and add an export warning only if exact preservation is impossible.

### 10.6 Complete interchange requirements

- Preserve XML comments, processing instructions, document order, and unknown attributes/elements when round-trip fidelity is enabled; distinguish fidelity mode from normalized export mode.
- Preserve `Definitions` child ordering according to the BPMN schema serializer, while retaining unknown extension order at its owner.
- Resolve QName-valued attributes using namespace URI bindings, not prefix text. Validate URI references, IDs, and fragment references before export.
- Keep semantic references (`sourceRef`, `targetRef`, `processRef`, `calledElementRef`, `messageRef`, and event/data references) separate from DI visual references (`bpmnElement`, `sourceElement`, `targetElement`).
- Support multiple `BPMNDiagram` objects and multiple planes when imported; let the user select the active view without deleting inactive diagrams.
- Preserve hidden semantic elements that have no DI and hidden DI elements that are intentionally quarantined; never create a fake shape merely to make the canvas look complete.
- Export a deterministic, canonical mode for tests and a fidelity-preserving mode for editing existing vendor files. Document which mode is used by each save command.
- Report every dropped, quarantined, normalized, or adapter-unavailable item in an import/export diagnostics manifest.

---

## 11. Testing Strategy

- **Connection unit tests**: table-driven cases covering every entry in the §3 compatibility matrix plus every rule code in §5.1/§5.2/§5.3 (both the accept and reject side of each rule).
- **Golden XML round-trip tests**: import a known-valid BPMN 2.0.2 file, export unchanged, and diff for semantic equivalence (DI waypoint order may legitimately differ, but re-imports of editor-generated output must be stable); validate every export against the schema chain in §10.3.
- **SVG rendering snapshot tests**: one test per event trigger × context × interrupting-state combination (§1.1 table), one per gateway marker, one per connector style/marker, one per activity marker combination, one per expanded/collapsed shape state, and one per Pool/Lane orientation (horizontal and vertical, §1.5/§10.4).
- **Outline-anchor interaction tests**: verify arbitrary outline attachment with grid snapping disabled and enabled for rectangles, rounded rectangles, circles, and diamonds; when enabled, snap in paper coordinates before outline projection and assert the committed endpoint remains on the outline. Cover creation, reconnection, endpoint-handle dragging, optional modifier bypass, zoom/pan transforms, and exact undo/redo after grid configuration changes.
- **Boundary Event position tests**: assert a Boundary Event's `Bounds` center lies on its host Activity's outline (DI-010) immediately after creation, after dragging along the border, and after the host is moved/resized/rotated (Pool orientation change); assert dragging cannot detach it from the border or leave it off-outline; assert re-parenting to a different host is a distinct, explicit gesture and updates `attachedToRef`, embedding (CT-005), and position atomically.
- **FlowNode collision tests**: assert non-boundary Events, Activities, and Gateways cannot be created, dropped, moved, or resized into a positive-area collision with a peer at the same containment level; verify configured clearance and zero-clearance edge touching; verify ancestor Pool/Lane/Sub-Process containment is exempt, Boundary Event/host outline overlap remains valid, rejected gestures roll back atomically with `DI-011`, and imported collisions are diagnosed rather than silently rewritten.
- **Fuzz/property tests**: generate random valid BPMN graphs from a small grammar and assert validators accept them; randomly mutate endpoints/containers/attributes and assert validators reject with the expected rule code; round-trip random valid models through XML export/import.
- **JointJS hierarchy tests**: reconstruct and validate root → Pool → nested Lane → expanded Sub-Process → child hierarchies; lane-less Pool children; Boundary Event attachment; scoped internal links; root-level cross-Pool Message Flows; collapsed Sub-Process hiding; and auxiliary Pool overlays. Verify deep move/delete/select, resize/fit, z-order, transactional valid/invalid reparenting, cycle prevention, exact undo/redo, and semantic/embedding parity after import. Assert BPMN Groups and temporary selection wrappers never become containment parents.
- **Sub-Process expand/collapse tests**: click/tap/keyboard-activate the collapsed `+` to expand; double-click only the expanded container body/border/header to collapse; verify nested child/connector/label/tool double-clicks do not collapse; verify Edit-mode `isExpanded`/bounds persistence and undo/redo; verify Readonly transient toggles do not dirty/serialize; preserve child semantics, BPMNDI, Boundary Events, external connectors, nested hierarchy, and deterministic fallback expanded bounds.
- **Regression fixtures**: keep one minimal fixture per rejected rule code (SF/MF/DA/D/DI-series) so future refactors of the validation engine can't silently drop a check.
- **Extension round-trip tests**: fixture each supported and unknown extension namespace with nested elements, qualified attributes, namespace-prefix changes, document-level extension definitions, and BPMNDI extension content; assert semantic preservation after import/export.
- **Context-menu tests**: verify the canvas-root capture handler suppresses the native browser menu on blank paper, every cell/link/label, edit tools, and HTML overlays in Edit and Readonly modes; mouse right-click, keyboard Context Menu/`Shift+F10`, and configured touch long-press must all invoke only the Blazor custom-menu path with correct paper-relative anchors.
- **Extension isolation tests**: verify an extension adapter cannot create invalid core BPMN connections, override required core markers, or bypass containment and pool-scope rules.
- **Extension security tests**: verify external entities, scripts, unsafe URLs, raw SVG injection, and untrusted schema fetches are rejected or sanitized.

---

## 12. Practical Pitfalls (implementation checklist of common mistakes)

1. **Do not confuse Pool and Lane.** Sequence Flow cannot cross Pools but may freely cross Lanes within the same Pool (§2.8).
2. **Do not treat SVG/DI endpoints as semantic endpoints.** `BPMNEdge.sourceElement`/`targetElement` may be visual overrides; the semantic `sourceRef`/`targetRef` stays authoritative (§10.1).
3. **Do not draw the conditional mini-diamond from a Gateway source.** It is only drawn when the source is an Activity (§10.1, §5.7 marker table).
4. **Do not allow Message Flow to a Gateway.** Gateways are excluded from `InteractionNode` endpoints (MF-003).
5. **Do not allow an Event Sub-Process as a Sequence Flow endpoint.** It is triggered only by its own typed Start Event (SF-005).
6. **Do not allow incoming Sequence Flow on a Boundary Event.** Its trigger is implicit from the attached Activity (SF-004).
7. **Do not allow a normal-flow (non-boundary) Error or Cancel Intermediate Event.** Error catch and Cancel catch are boundary-only (§2.4).
8. **Do not over-enforce recommendations as hard errors.** Multiple Start Events, multiple incoming End Events, and uncontrolled joins may be legal BPMN — warn (D005/D009/D016) unless a strict profile demands otherwise.
9. **Do not assume XSD validation is sufficient.** Many BPMN semantic rules (Event-Based Gateway homogeneity, Cancel-only-in-Transaction, data association direction) are prose-only and require a separate semantic validator layer (§10.3 step 4).
10. **Do not bake execution-engine-specific behavior into BPMN-core validation.** Keep any Camunda/Flowable/Zeebe-style rules in a separate, optional `PROF-` profile layer (§5.4).
11. **Do not mix a Lane's orientation with its parent Pool's.** Lock `isHorizontal` at the Pool level and propagate it (§1.5.3, DI-006).
12. **Do not let Data Association look like control flow.** It never carries a token and must render distinctly (dotted, thin arrowhead) from Sequence/Message Flow (DA-001).
13. **Do not discard unknown extensions.** Unsupported vendor elements must remain attached to their owning BPMN/DI element and survive a round trip (§6.1, §10.5).
14. **Do not identify extensions by XML prefix.** Compare namespace URI plus local name; prefixes may change during serialization (§6.1.2).
15. **Do not treat extension XML as trusted executable content.** Disable external entity resolution, sanitize SVG, and never execute vendor expressions while importing or rendering (§6.1.6).
16. **Do not let extension adapters redefine core BPMN semantics implicitly.** All custom connections, stencil entries, containment, and validation must be explicitly registered and namespaced.
17. **Do not reimplement a diagram framework in TypeScript.** Prefer the matching open-source JointJS graph, paper, geometry, anchor, connection-point, router, connector, tool, highlighter, event, and batching primitive; custom code must add BPMN behavior or fill a documented OSS gap.
18. **Do not let TypeScript invent editor configuration.** Public defaults and host overrides are Blazor-owned, versioned, validated, and sent before canvas initialization; TypeScript only derives runtime/JointJS options from the last accepted snapshot.
19. **Do not preserve poor structure to minimize a diff.** Refactor C# and TypeScript when needed to keep responsibilities cohesive, eliminate duplication, preserve strong typing, and prevent JointJS, interop, UI, validation, or serialization concerns from leaking across boundaries.
20. **Do not default to legacy language patterns.** Prefer the latest stable C#/TypeScript features supported by the pinned toolchain when they improve clarity or correctness; do not use preview syntax without an explicit compatibility decision.
21. **Do not maintain a parallel canvas containment tree.** Project BPMN ownership, Lane membership, Boundary attachment, and visual auxiliary ownership into JointJS embedding; validate and mutate both atomically.
22. **Do not treat a BPMN Group as a container.** A Group categorizes/frames elements but never embeds, owns, moves, or deletes the cells inside its bounds.

---

## 13. Implementation Checklist

### 13.1 Minimum viable compliance
- Semantic model supports Process + Collaboration + BPMNDI (including Pool/Lane `isHorizontal`).
- Definitions model includes target namespace, namespace bindings, imports, global ID index, root elements, diagrams, and required global definitions/references.
- SVG renderer generates all in-scope shapes/markers for both horizontal and vertical Pool/Lane orientations.
- Connection rules enforce Sequence Flow, Message Flow, Association, Data Association per §2/§5.
- Import/export BPMN XML with DI, validated against the schema chain (§10.3).
- Choreography/Conversation creation is rejected in the stencil (§8, §9.2).

### 13.2 Complete static BPMN editor compliance
- Full event-definition-per-context matrix implemented (§1.1, §8 research parity).
- Boundary event interrupting/non-interrupting rendering and validation.
- Boundary event position stays locked to its host Activity's outline border across creation, drag, host move/resize, and Pool orientation changes (§2.5, DI-010).
- Non-boundary Events, Activities, and Gateways cannot collide at the same containment level; creation, drag, resize, import diagnostics, and rollback enforce the configured clearance (`DI-011`).
- Context-menu requests for BPMN elements include a typed standard-property snapshot; context-menu property edits and same-category type switches are validated, atomic, undoable commands that preserve compatible identity/DI/connections and explicitly report cleared incompatible properties.
- Compensation and Cancel rules (Transaction-only) enforced.
- Link event pairing (name-matched, 1 catch : N throw) enforced.
- Event-Based Gateway configuration validated (homogeneity, no extra incoming, `instantiate`/Parallel rules).
- Gateway default/condition rules enforced per type (§2.6, §5.3 GW-series intent).
- Activity marker combinations and mutual exclusions correct (§1.2).
- Data Object/Data Store references and Data Association direction/transformation validated (§2.9).
- BPMNDI label, waypoint, bounds, and orientation validation complete (§10.2, §10.4).
- Non-destructive import for unsupported/excluded elements (§8, §9.2).
- Global Process/Task, Message, Signal, Error, Escalation, ItemDefinition, Interface/Operation, Resource, Property, and correlation references are modeled and resolved (§1.0, §5.2).
- ID, namespace, reference, lane membership, diagram/plane, label/style, and multiple-diagram validation is implemented (§5.2, §10.6).
- Validation pipeline runs XML safety → schema → references → containment → notation → semantic → extension/profile → DI/renderability layers (§5.5).
- Runtime-dependent constructs are preserved and linted without being falsely claimed as statically executable (§Compliance boundary).
- Extensibility layer supports `extensionElements`, `extensionDefinitions`, namespace-qualified attributes, unknown-extension preservation, and optional namespace adapters (§6.1).
- Core validation is isolated from optional extension/profile validation; extension diagnostics use `EXT-*` codes and cannot weaken BPMN-core rules.

### 13.3 Professional editor features
- TypeScript composes open-source JointJS primitives instead of duplicating graph/paper/geometry/routing/tool infrastructure; each custom substitute has a documented BPMN requirement or OSS capability gap.
- Blazor owns a strongly typed aggregate editor configuration; initial snapshot and atomic revisioned patches cover canvas/grid/snap, stencil/icons, interaction, theme/labels, routing, keyboard/touch, tools, accessibility, Readonly, execution overlays, and import/export behavior.
- C# nullable analysis and TypeScript strict mode are enforced; compiler/analyzer/linter warnings fail maintained-source builds except for narrow, documented suppressions.
- Feature acceptance includes necessary refactoring, removal of duplication/dead code, clear domain/JointJS/interop/UI boundaries, and use of current stable language constructs.
- JointJS embedding/grouping primitives implement the live visual hierarchy; semantic ownership/membership and embedding parity are validated after load, reparent, undo, and redo (§2.8.1).
- Live invalid-target highlighting while dragging a connector (§7).
- Custom Blazor context menus exclusively handle every canvas context-menu gesture; the browser native menu is unreachable within the canvas root (§7).
- Rule-code tooltips referencing this spec's IDs (SF/MF/DA/D/DI-series).
- Lint panel listing errors/warnings with quick fixes (§7).
- XML source view kept in sync with the diagram.
- Round-trip tests using the official OMG schemas (§11).
- Snapshot rendering tests for every SVG marker and both Pool/Lane orientations (§11).
- Accessibility: keyboard-driven connector creation, SVG `<title>`/`<desc>` elements, high-contrast marker styles.
- Extension developer API: namespace registry, typed extension adapters, declared reference mapping, property panels, safe SVG overlays, stencil/connection/containment hooks, and versioned extension validators.
- Fidelity-preserving and canonical export modes with diagnostics manifests (§10.6).
- Multiple diagram/plane view management, source XML inspection, undo/redo transactions, deterministic ID generation, and atomic save/export.

---

## 14. References
1. OMG, *Business Process Model and Notation (BPMN), Version 2.0.2*, formal/2013-12-09 — `https://www.omg.org/spec/BPMN/2.0.2/` (landing page), `https://www.omg.org/spec/BPMN/2.0.2/PDF` (normative text: §7.6.1–7.6.2 connection rules, §8.4 connecting objects/artifacts, §9.4 message flow, §10.5 events, §10.6 gateways, §12.3 DI depiction resolution).
2. OMG BPMN 2.0.2 XML schemas: `BPMN20.xsd` (`https://www.omg.org/spec/BPMN/20100501/BPMN20.xsd`), `Semantic.xsd` (`https://www.omg.org/spec/BPMN/20100501/Semantic.xsd`), `BPMNDI.xsd` (`https://www.omg.org/spec/BPMN/20100501/BPMNDI.xsd`), `DI.xsd` (`https://www.omg.org/spec/BPMN/20100501/DI.xsd`), `DC.xsd` (`https://www.omg.org/spec/BPMN/20100501/DC.xsd`).
3. bpmn.io `bpmn-js` (browser BPMN 2.0 viewer/modeler) — `https://github.com/bpmn-io/bpmn-js`; connection-rule prior art in `lib/features/rules/BpmnRules.js` — `https://github.com/bpmn-io/bpmn-js/blob/develop/lib/features/rules/BpmnRules.js`.
4. bpmn.io `bpmn-font` (build-time raw SVG symbol source only; no runtime dependency) — `https://github.com/bpmn-io/bpmn-font`, raw SVG asset `https://github.com/bpmn-io/bpmn-font/blob/main/dist/font/bpmn.svg`; license attribution: SIL Open Font License 1.1 — `https://github.com/bpmn-io/bpmn-font/blob/main/LICENSE`.
4. bpmn.io `bpmn-moddle` (BPMN 2.0 XML read/write + metamodel) — `https://github.com/bpmn-io/bpmn-moddle`; JSON metamodel — `https://github.com/bpmn-io/bpmn-moddle/blob/main/resources/bpmn/json/bpmn.json`.
5. Full research dossier with extended rule tables, endpoint matrices, and pitfalls: session file `research/document-and-detail-anything-that-is-use.md`.
