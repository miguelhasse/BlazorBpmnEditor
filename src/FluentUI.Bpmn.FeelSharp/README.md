# Fluent UI FeelSharp extension

Optional Fluent UI 5 editor using `FluentTextArea`, `FluentButton`, `FluentLabel`
and inline `FluentMessageBar` diagnostics.

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
accessibility references and overlay geometry update independently.

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
