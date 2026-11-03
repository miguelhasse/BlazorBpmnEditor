# Fluent UI FeelSharp extension

Optional Fluent UI 5 integration enhancing registered `FluentTextInput` /
`FluentTextArea` property controls in place, with advisory diagnostics.

```csharp
using Hasseware.FluentUI.AspNetCore.Components.Bpmn.FeelSharp;

builder.Services.AddFluentUIComponents();
builder.Services.AddFluentBpmnEditor().AddFluentBpmnFeelSharpEditor();
```

Managed parsing, highlighting, completion and guarded editor lifecycle are shared
with [Bpmn.FeelSharp](../Bpmn.FeelSharp/README.md). Its dependency-free DOM bridge
adapts to Fluent's native textarea inside the shadow root. No JavaScript editor
plugins or runtime npm language dependencies are used. Inline diagnostics do not
require a message-bar service provider; normal Fluent library assets load automatically.

Required/optional FEEL, literal fallback through the active kit, read-only rules,
advisory warnings, empty/null commits and one persistence `=` retain their existing
semantics. Up/Down navigate suggestions, Enter/Tab accept, Escape dismisses,
Ctrl/Cmd+Enter and focus loss commit. Native editing remains available if enhancement
cannot initialize; failures are logged.
The Fluent input is isolated while enhancement is active so delayed parent renders
cannot reapply old text. Generation changes recreate the control; native shadow-root
accessibility references update independently. Property inputs remain visible:
no duplicate editor, transparent text, or syntax overlay.

Ctrl+Space requests property completion; standalone/manager editors also provide
**Show FEEL suggestions**. Both support current-caret completion, including
empty input. Selected suggestions show function signatures/examples or separate
host variable documentation. Existing Enter/Tab acceptance and commits are unchanged;
composition never triggers explicit completion.

Use `AddFluentBpmnFeelSharpEditor(options => ...)` to configure the shared
`DiagnosticsObserver`, or the inherited `DiagnosticsChanged` component callback
for a directly rendered editor. Immutable diagnostic updates remain advisory,
include entry/generation identity, and have no guessed source ranges. See the
[shared API and accessibility guidance](../Bpmn.FeelSharp/README.md#advisory-diagnostics-api)
for observer lifetime, privacy, fallback, and test limitations.
Temporal function help explicitly notes that FeelSharp 1.3.0's facade returns
internal text as `StringValue`, not typed temporal results.

## Authoring surfaces

`FluentBpmnFeelSharpField` provides a prefix-free standalone value/draft adapter.
`FluentBpmnFeelSharpWorkspace` uses that field inside the shared transactional
workspace: Apply/Cancel, explicit interpreter authorization, categorized reference,
sample hints, editing actions, and host-owned proposal/sharing hooks. Shared workspace
chrome uses native accessible HTML controls; the editing control is Fluent.

Enable authoring features with `AddFluentBpmnFeelSharpEditor(options => ...)`:
ranked suggestions, local bindings, snippets, signature help, idle diagnostics,
configurable acceptance/automatic completion, density, and expanded editing all
share the [native options and contracts](../Bpmn.FeelSharp/README.md#opt-in-authoring-configuration).
Defaults preserve existing behavior. No language worker or editor plugin is added.
For startup-sensitive surfaces, combine `DeferInitialDiagnostics = true` with an
idle `DiagnosticsDelay`. Fields attach before their first/reset syntax check;
commits still flush diagnostics. The function reference renders only when opened.

With `ExpandedEditor = true`, expanded property editing opens through
`FluentBpmnToolbar` for the last-focused FEEL-capable property.
The manager uses the same `FluentBpmnDialog` shell as other solution dialogs:
header Close, footer Apply/Cancel, and lazy Editor, Reference, Variables/Sample,
and Evaluation tabs. Tabs, filters and actions use Fluent controls over the shared
transactional workspace. Editor has one expression surface and contextual diagnostics;
no repeated label, badge, persistent shortcut text or editor-button row. Optional
host actions are grouped under Tools. Ctrl+Space completion remains available.
It does not use `IDialogService` or require a FEEL-specific
`FluentDialogProvider`. Expanded drafts remain isolated until Apply and are rejected/cancelled
when source, revision, read-only, or lifetime changes. Workspace evaluator and
host actions must be explicitly supplied; DI alone does not authorize Run or sharing.

The editor and toolbar must share a `BpmnEditorScope`. Optional properties use a
visible leading `=` to detect FEEL in the existing control; no selectors are added.
Required-FEEL behavior is unchanged. The former `ModeSwitch` option and
`BpmnFeelSharpModeSwitch` enum have been removed; remove those registration settings.
All FEEL suggestion lists float in the browser top layer, escaping ancestor
overflow/stacking contexts, including within the manager. Browser Popover API
support is required; positioning tracks input scrolling and viewport changes.
`LiteralManagerBehavior` independently selects `RequireFeelMode` (default) or
`AllowConversionOnApply`. Conversion occurs only on explicit Apply.

Registration selects the Fluent FEEL editor only. Last FEEL registration wins;
explicit entry overrides retain precedence. Neither Core nor the existing UI kits
depend on this extension. Native FeelSharp and CamundaFeel remain alternatives.

Evaluation is separately opt-in through `AddBpmnFeelSharpEvaluation()` from
`Hasseware.AspNetCore.Components.Bpmn.FeelSharp`; no automatic execution or HTTP
endpoint is added. See that package's evaluation and cancellation guidance.

**License:** This extension's own code is MIT licensed; `LICENSE.txt` is included
in the NuGet package. FeelSharp 1.3.0 uses Apache 2.0 **with Commons Clause**, not MIT.
Review upstream restrictions before distribution or hosted use; this package does
not relicense its dependencies.
