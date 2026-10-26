# Writing a UI kit package

Reference `Hasseware.AspNetCore.Components.Bpmn`, not either existing editor kit. A kit supplies
markup, CSS and default registrations. Core owns state, commands, validation, property descriptors,
notation and canvas interop. [Composing an editor](composing-an-editor.md) explains the layout contract.

## Derive from the part base

Registered parts render through `BpmnPart` / `DynamicComponent` without explicit parameters.
They must be concrete components deriving from the required Core base and work with default
parameter values. The base reads session/state from cascades and supplies commands and localized
labels. For example, `MyToolbar.razor`:

```razor
@using Hasseware.AspNetCore.Components.Bpmn
@inherits BpmnToolbarBase

<div role="toolbar" aria-label="@EffectiveAriaLabel" class="@CssClass("my-toolbar")"
     @attributes="AdditionalAttributes">
    <button type="button" disabled="@(!CanUndo)" @onclick="UndoAsync">@Text("ContextMenu.Command.Undo")</button>
    <button type="button" disabled="@(!CanRedo)" @onclick="RedoAsync">@Text("ContextMenu.Command.Redo")</button>
    <output>@ZoomLabel</output>
    @ChildContent
</div>
```

Keep behavior in the bases: do not copy history, property grouping, stencil selection or overlay
state machines into markup. If overriding lifecycle methods, call the base implementation so parent
checks, state transitions and focus handling still run.

| `BpmnEditorPart` | Required base | Placement |
|---|---|---|
| `Toolbar` | `BpmnToolbarBase` | Editor scope |
| `Stencil` | `BpmnStencilPanelBase` | Editor scope |
| `PropertyPanel` | `BpmnPropertyPanelBase` | Editor scope |
| `ValidationPanel` | `BpmnValidationPanelBase` | Editor scope |
| `DiagramNavigator` | `BpmnDiagramNavigatorBase` | Editor scope |
| `ContextMenu` | `BpmnContextMenuBase` | Canvas host inside editor scope |
| `ContextualActions` | `BpmnContextualActionsBase` | Canvas host inside editor scope |
| `LabelEditor` | `BpmnLabelEditorBase` | Canvas host inside editor scope |
| `AccessibleTree` | `BpmnAccessibleTreeBase` | Canvas host inside editor scope |
| `ImportOffer` | `BpmnImportOfferBase` | Editor scope |
| `CalledElementPicker` | `BpmnCalledElementPickerBase` | Editor scope |
| `ShortcutHelp` | `BpmnShortcutHelpBase` | Editor scope |

`BpmnEditorComponentOptions.RequiredBase(part)` exposes this contract. The four canvas bases
inherit `BpmnCanvasWidgetBase<TState>`: retain anchor placement, focus target and keyboard behavior.
Core supplies `BpmnLabelEditor` and `BpmnAccessibleTree`; reuse them unless replacing their markup
is necessary. `BpmnTemplateChooserBase` and `BpmnReferenceBrowserBase` support auxiliary chrome,
but are not enum registry parts.

A replacement inline label editor should retain a `.bpmn-label-editor` wrapper with
`data-bpmn-element="@Edit.ElementId"` and a native `textarea` inside it. The canvas uses that
markup to size the input to its content and keep the editor anchored during scrolling and zooming.
Keep the base's focus target, text binding, and commit/cancel handlers.

Context menus retain a `.bpmn-menu` wrapper with
`data-bpmn-element="@Selected?.ElementId"` and the base's `PositionStyle`. The canvas keeps this
wrapper at the target's bottom-left through scrolling, panning, zooming and rerenders; blank-canvas
menus follow the invocation's diagram point. Position updates do not reopen the menu or move focus.
Keep geometry in the canvas rather than adding kit-owned JavaScript.

Render Switch Type as a labelled dropdown when the snapshot permits switching and supplies more
than one option. Use `SelectedTypeValue`, `TypeValue` and `SelectTypeAsync` from
`BpmnContextMenuBase`; key the selector by `(Selected.ElementId, TypeRevision)` so a refused switch
restores the snapshot's current type. Options include both type and event definition, and their
display text comes from `Display.TypeOption`. Type switches keep the menu open; command buttons
retain their normal dismissal. Icon-only commands need localized accessible names and tooltips.

## Register defaults, not host overrides

Use `UseDefault<TComponent>`, never `Use<TComponent>`, in a kit registration extension. Defaults
are only-if-unset and cannot revive a host-removed part. Give your composition its own
`BpmnKitDefaults` so it remains stable when another kit registers in the same container.
The following minimal example registers a toolbar; a full kit adds its other parts and editors:

```csharp
using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class AcmeBpmnDefaults
{
    public static BpmnKitDefaults Kit { get; } = CreateKit();

    private static BpmnKitDefaults CreateKit()
    {
        var kit = new BpmnKitDefaults();
        kit.Components.UseDefault<MyToolbar>(BpmnEditorPart.Toolbar);
        return kit;
    }

    public static void Register(BpmnOptions options) =>
        options.Components.UseDefault<MyToolbar>(BpmnEditorPart.Toolbar);
}

public static class AcmeBpmnServices
{
    public static IServiceCollection AddAcmeBpmnEditor(this IServiceCollection services)
    {
        services.AddBpmnComponents();
        services.PostConfigure<BpmnOptions>(AcmeBpmnDefaults.Register);
        return services;
    }
}
```

Your composition wraps its layout in
`<BpmnEditorScope KitDefaults="@AcmeBpmnDefaults.Kit">`, renders one `BpmnEditorCanvas`, and uses
`BpmnPart` for chrome. Preserve the six slots described in the composition guide if offering the
same customization surface as the built-in kits. Let the host register the underlying UI framework's
services/providers; do not choose host lifetimes from a kit extension.

## Property editors

Property entries are a separate registry, not `BpmnEditorPart` values. Kit editors should derive
from `BpmnPropertyEntryComponent`, consume its typed `Scope` / `Context`, honor `ReadOnly`, and submit
through `SubmitAsync`. Reset local drafts when context source/revision changes. The registry accepts
`IComponent` types with a `BpmnPropertyEntryContext` parameter named `Context`; using the base provides
this contract and enforces the required `BpmnPropertyScopeProvider` parent.

Register kind defaults in **both** `kit.Editors` and `options.Properties.Editors` using
`UseDefaultEditor<TComponent>(BpmnPropertyEntryKind.Text)` (and other kinds), plus
`UseDefaultFeelEditor<TComponent>()` for expressions. The kinds live in
`Hasseware.AspNetCore.Components.Bpmn.Editing`. A `BpmnPropertyScopeProvider` inside an editor uses
the session's merged registry automatically; otherwise it uses parent/application defaults.

Host overrides use `AddEditor<TComponent>(kind)`, `AddEditor<TComponent>(editorKey)`,
`AddEditor<TComponent>(predicate)` and `AddFeelEditor<TComponent>()`. Resolution is explicit key,
last matching predicate, FEEL editor, explicit kind, kit/default kind, then Core's `BpmnReadOnlyEntry`.
A property-panel `EntryTemplate` bypasses registry rendering. The optional Bpmn.Feel package can
replace either kit's FEEL default without depending on either kit.

## Dialog rendering

All built-in modal parts use one header Close button and native Escape dismissal. Footer actions
such as Cancel, Save, Open, Deny and Allow remain separate commands; do not add another generic
Close action to the footer. Close/Escape must never accept an import, save a draft or approve an
agent request.

Standard `BpmnDialog` renders a native `<dialog>` and derives from Core's `BpmnDialogBase`.
Core owns its small TypeScript modal lifecycle helper; it works without a canvas instance.
`FluentBpmnDialog` uses inline `FluentDialog` and `FluentDialogBody`: `TitleTemplate` supplies
heading content, `TitleActionTemplate` holds the wrapper-owned Close button, and `ActionTemplate`
supplies footer actions. Neither kit requires a dialog service/provider.

Keep wrappers mounted while setting `Visible=false`, allowing the native modal to close before
host removal. Use `OnDismiss` for the existing non-accepting session action and `OnClosed` when
focus restoration must wait for native modal release. Native modal behavior owns initial focus
and Tab containment; no FirstButton/LastButton references or sentinel spans are needed. Use
`FocusAsync` to focus the dialog's content region when replacing focused form content. This avoids leaving
focus on the document body or redirecting an in-flight Enter keypress onto Close. Use
stable form IDs and explicit form association for submit actions rendered outside their form.
External-change offers remain nonmodal alerts.

## Performance

`BpmnEditorComponentBase.ShouldRenderOnStateChange(previous, current)` defaults to `true`.
Presentation bases narrow it to the immutable state slices their built-in markup reads. If your
markup reads more state, widen that predicate. For example, a toolbar adding a selection-count
badge must also watch selection:

```csharp
protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
    base.ShouldRenderOnStateChange(previous, current) || previous.Selection != current.Selection;
```

Override it to return `true` when dependencies cannot be tracked safely. Filters apply only to
state-only cascade changes; first render, other parameter updates, local events/StateHasChanged,
async completion and culture changes still render. If overriding `ShouldRender` itself, combine
your condition with `base.ShouldRender()` so the one-shot state filter is consumed correctly.

Property providers should be stable functions of their snapshot inputs: groups/entry contexts are
cached until inputs change. Keep context revision resets in entry editors, including refused edits.
See [Managed editor state renders](performance.md#managed-editor-state-renders) for cache keys,
conservative exceptions and the both-kit render-budget harness.

## Ownership and architecture

- Core owns all shared behavior. No kit references another kit or Core internals.
- `BpmnCanvas` alone owns canvas `IJSObjectReference` / `DotNetObjectReference` handles;
  `BpmnCanvasEventBridge` alone declares canvas `[JSInvokable]` methods. Core's `BpmnDialogBase`
  separately owns native modal lifecycle interop without a canvas handle. Kits own no JS modules.
  Bpmn.Feel is a separate optional interop owner, not a kit implementation shortcut.
- BPMN notation comes from `BpmnIconRegistry` / `BpmnIcon`, not a UI framework's generic icons.
  Framework icons are appropriate for undo, close and other non-BPMN chrome.
- Core ships canvas/theme/label-editor styles. Kits ship their own scoped-to-root chrome CSS and
  style `.bpmn-editor__canvas` placement. Theme custom properties must reach the canvas itself;
  ancestor-only overrides lose to canvas-local defaults. Preserve forced-colors behavior.
- Keep native landmarks, labels, input/help/error relationships, keyboard access and overlay
  client-coordinate placement when swapping controls.

Before publishing, test required-parent exceptions, parameterless registry rendering, command
routing, host override precedence, both-kit coexistence, and assembly/interop ownership.
The built-in kit suites share parity tests under `tests` for these contracts.

## Localize kit-only strings

Use a marker class and adjacent `.resx` for kit-only text through `IStringLocalizer<TMarker>`.
Shared base labels already use Core's `BpmnStrings`; do not duplicate them in every kit.
Leave `ResourcesPath` empty for the supplied packages. If the assembly name differs from the
resource namespace, set the project's `RootNamespace` and declare
`Microsoft.Extensions.Localization.RootNamespaceAttribute` in the assembly, as the two kits do.

In this repository, set `EnableXlfLocalization=true` to opt into XliffTasks, commit the
`Resources\xlf` files, and run `dotnet build /t:UpdateXlf` after source text changes.
An external kit must provide the equivalent XliffTasks reference/build setup itself; repository
`src\Directory.Build.targets` is not shipped as consumer build logic.
See [Localization](localization.md) for feeds, cultures and host factory overrides.
