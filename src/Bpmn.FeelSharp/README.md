# FeelSharp FEEL extension

Optional `Hasseware.AspNetCore.Components.Bpmn.FeelSharp` provides a Blazor Server/WebAssembly
property editor and separately registered server-side interpretation.
The optional [Fluent UI extension](../FluentUI.Bpmn.FeelSharp/README.md) reuses this
package's managed behavior with FluentTextArea and Fluent chrome. This package
itself has no Fluent dependency.

```csharp
using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

builder.Services.AddFluentBpmnEditor().AddBpmnFeelSharpEditor();
// Standard UI kit: builder.Services.AddBpmnEditor().AddBpmnFeelSharpEditor();
```

The editor preserves required/optional FEEL modes, one leading persistence `=`, null for
empty committed expressions, read-only restrictions, binding errors, and advisory syntax
warnings. Invalid expressions can still be committed. Blur and Ctrl/Cmd+Enter commit edits.
Expression and literal values edit directly in the host kit's registered property
input or textarea. Single-line properties remain single-line. There is no separate
expression textarea, syntax overlay, badge, inline button, or permanent shortcut
chrome in a property panel. Completion appears only as an attached suggestion list.

Optional properties use a visible leading `=` in the same input to detect FEEL;
without it, text is literal. No Literal/FEEL selectors are rendered.
Required expressions remain FEEL regardless of a visible prefix.
The former `ModeSwitch` option and `BpmnFeelSharpModeSwitch` enum were removed;
remove those settings from existing registrations.

All completion lists use native manual popovers in the browser top layer. They
escape overflow clipping and stacking contexts, including inside the manager,
and track input position on scroll/resize without stealing focus. Browser
Popover API support is required for floating completion.

FeelSharp 1.3.0 `ParseExpression` supplies diagnostics without executing expressions. Names
need not have runtime values to parse successfully. A small managed tokenizer renders
escaped highlights in standalone/manager editors; completion uses FEEL keywords, a curated supported function
catalog, and `Context.Variables` (including nested members). This is not a claim of full
CodeMirror feature or Camunda grammar parity.
Variable hints take precedence over same-named catalog functions. Function completion
preserves existing argument parentheses.
Completion inside an existing multiword name replaces the whole matching name,
without duplicating its remaining words.
Delayed fallback input and commit callbacks are ignored after an entry generation
changes, just like enhanced native snapshots.

Native textarea editing supplies caret, selection, clipboard, undo/redo, and IME behavior.
A dependency-free TypeScript DOM bridge synchronizes selection/composition/scroll and
applies completion edits; it contains no FEEL parser, evaluator, or JavaScript editor
plugin. Up/Down navigate suggestions, Enter/Tab accept, and Escape dismisses them.
Tab moves focus normally when no completion is open. Without enhancement the visible
native textarea remains usable, with managed warnings and commits but no caret completion.

## Suggestions and function help

Ctrl+Space explicitly requests suggestions at the current property caret.
Standalone/manager editors also provide **Show FEEL suggestions** at the current
native caret, including an empty expression. The button is disabled when enhancement
is unavailable or the editor is read-only. IME composition is never intercepted;
use the button when Ctrl+Space conflicts with an input method or platform shortcut.
Suggestions retain the existing alphabetical order and 12-item limit.
Refreshing variable metadata preserves an already-open explicit suggestion list.
Blur or commit cancels queued explicit completion so suggestions do not reopen
after focus leaves the input.

Selected functions show a representative signature, localized description, and
verified example. The 38-function catalogue is tested against the restricted
FeelSharp 1.3.0 interpreter; it is not an exhaustive list of overloads or a Camunda
compatibility guarantee. Default insertion still uses `name()`. Opt-in authoring features add structural
snippets and active-call parameter guidance. Variable `Detail` and `Info` remain
separate; host documentation is rendered as text, never raw HTML.

**Temporal result caveat:** FeelSharp 1.3.0's public facade exposes results of
`date`, `date and time`, `duration`, `time`, `today`, and `now` as `StringValue`
containing internal text (for example `ValDate ...`), not typed temporal values.
Their help includes this limitation. Do not parse this internal text as a stable
serialization contract.

## Advisory diagnostics API

Both native and Fluent components inherit the immutable `Diagnostics` property
and `DiagnosticsChanged` callback. Registry-created editors can also be observed
through shared registration options:

```csharp
builder.Services.AddBpmnFeelSharpEditor(options =>
{
    options.DiagnosticsObserver = update =>
    {
        // Observe ElementId, EntryId, Revision, Generation and Diagnostics.
        // Route to a host-owned issue surface without changing commit policy.
        return Task.CompletedTask;
    };
});
```

For a directly rendered component, supply `DiagnosticsChanged` alongside its
normal `Context` inside a `BpmnPropertyScopeProvider`.
Updates include initial analysis, text changes, and entry/mode/reset changes;
valid expressions and literal mode publish an empty collection. Caret-only
movement and suggestion navigation do not republish. Composition defers analysis
until finalized text arrives; old generations and disposed editors are ignored.
Component callback runs before the optional registration observer; a context
change during that callback suppresses delivery of the superseded observer update.

`FEEL.Syntax` diagnostics have warning severity and the original upstream message.
`Start` and `Length` are absent because this parser supplies no trustworthy range.
Binding errors remain separate, and warnings never block commits. No diagnostics
event is emitted from a render getter. Callback/observer exceptions propagate
instead of silently disappearing.

Options apply to both renderings. Do not capture per-user mutable state in
application-wide options. Updates omit draft text and variable values, but parser
messages can include expression fragments: treat them as potentially sensitive
and do not forward them externally without explicit host configuration.

### Accessibility and platform coverage

The property control remains its original native textbox or multiline textbox, with textual warnings,
help, polite suggestion-count announcements, and selected-function documentation.
Keyboard navigation scrolls only the suggestion list. Fluent shadow controls use
reflected element references where supported; older browsers receive text through
`aria-description` rather than invalid cross-shadow ID relationships. That fallback
does not provide the same active-option relationship as modern browsers.
Forced colors retain readable native text. Hosts can override
`--bpmn-feel-string` and `--bpmn-feel-number` syntax colors.

Bridge regressions cover coalescing, Ctrl+Space modifier/IME guards, source/generation
checks and both ARIA paths. Published Server and trimmed WebAssembly browser smoke
checks cover both renderings: explicit completion, function help, list scrolling,
advisory diagnostics and commits, reset/read-only transitions, and native
`execCommand` undo/redo. Fluent reflected ARIA references were checked in Chromium.
These checks include synthetic keyboard/input events, not real screen-reader or
operating-system IME sessions. Clipboard, forced-color visual checks, actual
keyboard gestures, and supported browser/AT combinations still require manual
testing; these checks do not establish accessibility compliance.

## Opt-in authoring configuration

Existing ordering, automatic completion, Enter/Tab acceptance, density, and immediate
diagnostics remain defaults. Both renderings share these options:

```csharp
builder.Services.AddBpmnFeelSharpEditor(options =>
{
    options.RankCompletions = true;
    options.LocalVariables = true;
    options.SignatureHelp = true;
    options.Snippets = true;
    options.AcceptTab = false; // Keep Tab as focus traversal.
    options.AutomaticCompletion = false; // Ctrl+Space/button still work.
    options.DiagnosticsDelay = TimeSpan.FromMilliseconds(200);
    options.DeferInitialDiagnostics = true; // Render/attach before initial and reset checks.
    options.Density = BpmnFeelSharpDensity.Comfortable;
    options.ExpandedEditor = true;
});
```

Ranking prefers variables, then functions, then structural keywords. Matching is
case-tolerant while identity and insertion retain exact names. Conservative local
hints cover supported `for`/`some`/`every` bindings, function parameters, and prior
context keys; types remain unknown and this is not a semantic FEEL parser.
Nested call/collection delimiters, strings, temporal literals, and comments are
excluded from active-parameter comma counting. Metadata refresh retains explicit
completion. Snippets select the first editable placeholder as one native undoable
edit; they do not implement a multi-placeholder snippet session.

Tokenized text is reused between highlighting, completion, and signature help.
Opt-in idle diagnostics avoid parsing every acknowledged keystroke; commits flush
pending analysis, resets/disposal cancel it, and composition defers it. Parsing
itself is synchronous. `DiagnosticsDelay` alone preserves immediate initial/reset
checks. Set `DeferInitialDiagnostics` to schedule those checks after DOM attachment
(or the first read-only/literal render), followed by the configured idle delay.
A zero delay still yields before deferred initial checks. Until analysis completes,
diagnostics are empty, not proof of valid syntax; callbacks publish the eventual
result. Commits always flush pending analysis. Parser engines initialize lazily:
empty and literal-only fields do not build an engine. Function references
mount only when the Reference tab is first selected.

Completion streams current metadata into at most twelve ordered candidates, while
immutable built-ins are prepared once. Host variable/function mutations are not
hidden behind reference-only caches. Duplicate bridge snapshots are suppressed;
caret changes, composition, explicit completion and commits retain their semantics.
Workspace drafts avoid rerendering previously visited reference tabs, except when
clearing a displayed evaluation result. Fluent geometry and external popup positioning
coalesce per frame without delaying read-only/ARIA updates. No shared evaluator engine,
global expression cache or JavaScript editor plugin is added.
See [managed and browser measurements](../../tests/Bpmn.Benchmarks/README.md#feelsharp-authoring)
for reproducible allocation comparisons and remaining synchronous-engine limits.

A reproducible 1/10/100 KB and 1,000-variable corpus is in
`EnhancementTests`: initial development runs observed roughly 20–22 / 392–439 /
1,295–1,581 ms parse time with approximately 3 / 27 / 267 MB total thread allocations
including token/completion/assertion work. These are cold, concurrent-test-host
observations, not benchmarks or supported latency/input-size guarantees.
No incremental parser or arbitrary expression-size cap is introduced.

### Scoped and sample variables

`AddBpmnFeelSharpScopedVariables()` explicitly replaces the default document-wide
provider with containment-based inference. Parent/owning-scope outputs and selected
task inputs are hints; unrelated process/child-local hints and sibling task inputs
are excluded. Inner/selected definitions shadow outer definitions deterministically.
`Source` identifies the defining BPMN element, and `IsInferred` distinguishes hints
from host declarations. Scope does **not** establish execution order or runtime existence.
Hosts may still replace `IBpmnFeelVariableProvider` themselves.

`FeelSharpSampleVariables.FromJson(json)` derives nested members/types without
execution. `FromSchema(json)` accepts only inline `type`, `properties`, `items`,
`description`, `title`, and `$schema` metadata. References, combinators, unknown
keywords/types, non-object roots, and malformed data fail explicitly. Inference is
bounded to JSON depth 32 and 1,000 visited nodes; heterogeneous member types become
unknown. ISO-looking strings stay strings. Host declarations outrank sample hints.

### Standalone and transactional workspaces

`BpmnFeelSharpField` provides `Value`/`ValueChanged`, `DraftChanged`, variables,
diagnostics, read-only mode, rows, and reset `Revision`. Values are prefix-free by
default; enable `PersistencePrefix` for BPMN-style values. Drafts do not invoke
the commit callback. `CaptureAsync` reads native text/selection; `ApplyDraftAsync`
applies explicit host-accepted edits through source/generation/native-undo guards.
Pass the original `CaptureAsync` result, not a reconstructed/serialized draft;
opaque per-generation provenance prevents old captures from applying after an
identical-value reset or to another field.
The Fluent package supplies `FluentBpmnFeelSharpField`.

`BpmnFeelSharpWorkspace` and `FluentBpmnFeelSharpWorkspace` hold isolated drafts.
Only `Applied` persists a value; Cancel/reset does not. They include searchable
categorized function reference, variable paths/provenance, sample inference,
conservative selected-block indentation, and parse-verified terminal delimiter fixes.
Multiline strings/comments are not blindly reindented. Full semantic formatting is
unavailable. CSS overrides include `--bpmn-feel-padding`, `--bpmn-feel-function`,
`--bpmn-feel-temporal`, and workspace foreground/background variables.

With `ExpandedEditor = true`, the BPMN toolbar offers **Manage FEEL expression**
for the last-focused eligible property. Moving to the toolbar retains the target;
selection/document changes, row removal/reordering, hidden panels, disposal, and
read-only state invalidate it. Each editor owns its own target. Directly rendered
property controls need a `BpmnEditorScope` and a kit toolbar for this action.
Standalone fields retain their explicit expansion action.

The manager uses the solution's `BpmnDialog` / `FluentBpmnDialog` shells, standard
header Close and footer Apply/Cancel, and Editor, Reference, Variables/Sample,
and Evaluation tabs. Advanced panels mount on first visit; switching tabs preserves
draft text, native undo, reference search, sample data, and results.
The Editor tab contains one expression surface and contextual diagnostics, with
no repeated field heading, FEEL badge, completion button or permanent shortcut
text. Ctrl+Space still opens completion; optional host actions live under Tools.
Evaluation safety/authorization information stays in Evaluation, never the Editor tab.
No FEEL-specific dialog service provider is required.
Inline persistence is suspended while the isolated modal draft is open; Apply is
guarded against source, element, revision, read-only, and generation changes.

Optional literal properties default to a disabled manager action until switched
to FEEL. Set `LiteralManagerBehavior` to
`BpmnFeelSharpLiteralManagerBehavior.AllowConversionOnApply` to author an expression
from literal text; opening or cancelling never changes the property. Only Apply
converts it to FEEL. Prefix-only field editing does not change this policy.

### Explicit evaluation and host actions

Supply a workspace's `Evaluator` explicitly to authorize Run. A DI evaluator by
itself does not authorize a workspace. Sample context remains ephemeral/local,
clears on source/context/reset changes, and is parsed as JSON objects with decimal
numbers, nulls, lists, nested contexts, and unchanged strings. Results show their
actual root FeelValue type and JSON content, distinguishing null, false, empty lists,
temporal facade strings, and errors. Run never writes process data or invokes BPMN.
Results from replaced/disposed/edited drafts are rejected. Copy rejects stale
results and reports clipboard failures visibly with host logging; host callback
failures propagate through normal host error handling.

`BpmnFeelSharpHostActions` supplies optional explicit Suggest, Share, and Collaborate
callbacks. Proposed text requires a separate Accept action and matching source;
it cannot overwrite a newer draft. No transport, AI SDK, model, automatic request,
or data-retention service is provided. Hosts own authorization, privacy, revision
protocols, cancellation, and observer lifetimes.

`BpmnFeelSharpCapabilities` exposes verified unsupported capabilities:
parse-only unary validation, semantic formatting, and hard execution timeouts.
Experimental `EvaluateUnaryTests` is an **explicit evaluation API**, not validation.
The pinned corpus records `> 18` comparisons, plus upstream limitations: wildcard
`-` fails parsing and `[1..10]` with input 3 returns false. Consequently no unary-test
editing dialect or DMN/Camunda compatibility is advertised.

Host function documentation can be supplied through editor `Functions`; metadata
alone never enables execution. `AddBpmnFeelSharpEvaluation(options => ...)` accepts
explicit trusted `ICustomFunctionProvider` and `ICustomValueMapper` instances.
Automatic discovery and external functions remain disabled. This is an allowlist,
not isolation from code inside a trusted provider. Each request still owns an engine.
Custom evaluators expose their own `Profile`; do not label them as a Camunda runtime.

The demos `/feel-authoring-server` and `/feel-authoring` exercise Server and trimmed
WebAssembly. Their sample interpreter must be explicitly authorized before Run.

### Support and verification boundaries

| Surface | Verified scope | Not established |
| --- | --- | --- |
| Native/Fluent fields and expanded workspaces | Published Server and trimmed WebAssembly in Chromium-based browser; Apply/Cancel, reset/read-only, explicit Run and root/nested result distinctions | Other browser engines or Camunda conformance |
| Native editing | Guarded replacements, first snippet placeholder, native undo; matching input/highlight metrics | Real OS IME, mobile keyboards, exhaustive undo histories |
| Accessibility | Standard solution modal shells, ARIA direct-reference/fallback bridge, keyboard suggestions and forced-color CSS | Screen-reader certification, real forced-color/AT sessions |
| Host functions/mappers and unary tests | Explicit allowlist API and pinned compatibility corpus | Arbitrary provider/name/type compatibility or sandboxing |

Host function names and mapper values must pass the host's own compatibility
corpus. Documentation metadata does not prove that the pinned parser/interpreter
accepts every custom name or implementation. Localization adds English source
messages and synchronized `state="new"` XLIFF entries; new translations remain
translator work. License boundaries are unchanged.

## Optional server evaluation

```csharp
builder.Services.AddBpmnFeelSharpEvaluation();

var app = builder.Build();
var evaluator = app.Services.GetRequiredService<IBpmnFeelSharpEvaluation>();
var result = evaluator.Evaluate("= amount > 100",
    new Dictionary<string, object?> { ["amount"] = 125 });
if (result.IsSuccess)
{
    // Inspect the actual FeelValue type, including successful FEEL null.
    // Temporal results have the facade limitation described above.
}
else
{
    // result.Error contains the upstream failure, not a success-shaped fallback.
}
```

Editor registration never enables evaluation. Evaluation registration never changes the
editor or adds an HTTP endpoint/BPMN executor. Each call owns a fresh interpreter with
automatic provider discovery and external functions disabled. Cancellation is checked
before and after the synchronous call; it cannot interrupt the upstream interpreter.
FEEL operations may return successful null per language semantics rather than errors.

**Evaluation is not a sandbox:** use trusted expressions and context data, especially
when supplying rich .NET objects. Hosts must control resource use and exposure.
No expression-tree compilation is used.

## Dependencies and license

Core/UI kits do not depend on this package. CamundaFeel remains an alternative.
If both optional editors are registered, the last FEEL registration wins; explicit
entry overrides retain the registry's normal precedence.

This extension's own code is MIT licensed; `LICENSE.txt` is included in the NuGet
package. Dependencies retain their own licenses.

FeelSharp 1.3.0 bundles FSharp.Core and FParsec. Its actual packaged `LICENSE` is
**Apache License 2.0 with Commons Clause**, not the MIT badge shown in its README.
Review the [upstream package](https://www.nuget.org/packages/FeelSharp/1.3.0) license and
third-party notices before commercial distribution or hosted use. This extension does
not relicense upstream binaries. They are supplied by the NuGet dependency, not copied
into this extension package.

The private `Bpmn.FeelSharp.Scripts` project builds the small DOM bridge with TypeScript
and esbuild; there are no runtime npm dependencies. Its output is served at
`_content/Hasseware.AspNetCore.Components.Bpmn.FeelSharp/feelsharp-editor.js`.
