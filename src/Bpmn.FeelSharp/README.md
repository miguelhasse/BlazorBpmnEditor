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

The editor preserves required/optional FEEL toggles, one leading persistence `=`, null for
empty committed expressions, read-only restrictions, binding errors, and advisory syntax
warnings. Invalid expressions can still be committed. Blur and Ctrl/Cmd+Enter commit edits.
Optional literal values use the host UI kit's normal property editor.

FeelSharp 1.3.0 `ParseExpression` supplies diagnostics without executing expressions. Names
need not have runtime values to parse successfully. A small managed tokenizer renders
escaped inline highlights; completion uses FEEL keywords, a curated supported function
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

## Optional server evaluation

```csharp
builder.Services.AddBpmnFeelSharpEvaluation();

var app = builder.Build();
var evaluator = app.Services.GetRequiredService<IBpmnFeelSharpEvaluation>();
var result = evaluator.Evaluate("= amount > 100",
    new Dictionary<string, object?> { ["amount"] = 125 });
if (result.IsSuccess)
{
    // result.Value is a typed FeelSharp FeelValue, including successful FEEL null.
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
