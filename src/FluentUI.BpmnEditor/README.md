# Hasseware.FluentUI.AspNetCore.Components.BpmnEditor

Fluent UI **5.0.0** BPMN editor chrome for .NET 10 Blazor. Depends on Core, Fluent UI and
Fluent UI Icons only. No dependency on the standard HTML editor package; no kit-owned JavaScript modules.
Components use namespace `Hasseware.FluentUI.AspNetCore.Components.Bpmn`.

## Host setup

```csharp
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.FluentUI.AspNetCore.Components;

builder.Services.AddFluentUIComponents();
builder.Services.AddFluentBpmnEditor();
// Optional rich FEEL editor: builder.Services.AddBpmnFeelEditor();
```

**The host must call `AddFluentUIComponents()` and add its Fluent providers itself.**
`AddFluentBpmnEditor()` never calls Fluent's registration extension or chooses host service lifetimes.
Add providers such as `FluentDialogProvider`, `FluentTooltipProvider`, `FluentMessageBarProvider`
and `FluentToastProvider` when using their services, including components supplied in editor slots.
Kit dialogs use inline `FluentDialog` through `FluentBpmnDialog`, preserving editor cascades
rather than reparenting content through a dialog service. The wrapper synchronizes visibility with
`ShowAsync()` / `HideAsync()` and handles native dismissal directly, including Escape, so session
state, modal cleanup, and focus restoration stay in sync. Fluent controls still require host services.
Fluent's own scripts and CSS auto-load; do not manually add Fluent library asset links.

Link the **kit stylesheet** in host HTML (after Core's canvas styles):

```html
<link rel="stylesheet" href="_content/Hasseware.FluentUI.AspNetCore.Components.BpmnEditor/css/FluentBpmnEditor.css" />
```

Core's canvas bundle retains its existing static-web-assets/initializer setup. Do not link the
standard editor stylesheet when using only this kit.

```razor
@using Hasseware.AspNetCore.Components.Bpmn.Model
@using Hasseware.FluentUI.AspNetCore.Components.Bpmn

<FluentBpmnEditor Document="@document" ShowStencil="true" />
```

Keep the whole editor inside one interactive render-mode boundary. Scope cascades do not cross
boundaries. As with the standard composition, `ShowStencil` defaults to false and `ShowPanel`
to true. The demo's `/editor`, `/gallery` use this kit; `/editor?plain=true` uses the standard kit in the same
container (each composition supplies its own kit defaults).

## Components and verified v5 APIs

Each editor part derives its matching Core base and reads Core session/state. Native structure
retains landmarks, names, list/group semantics and positioning. Concrete controls below were
checked against the installed **5.0.0** assembly, not v4 documentation.

| Kit components | Fluent v5 controls | Non-BPMN icons (`Icons.Regular.Size20`) |
|---|---|---|
| `FluentBpmnEditor` | Core `BpmnEditorScope`, `BpmnEditorCanvas`, `BpmnPart`; native layout landmarks | None |
| `FluentBpmnToolbar` | `FluentButton` (`IconStart`, `IconOnly`, `ButtonAppearance.Subtle`) | `ZoomOut`, `ZoomIn`, `ArrowReset`, `ArrowFit`, `ArrowUndo`, `ArrowRedo`, `QuestionCircle` |
| `FluentBpmnStencilPanel` | `FluentTextInput`; native draggable/keyboard options | None: BPMN silhouettes/glyphs use Core `BpmnStencilPresentation` / `BpmnIconRegistry` |
| `FluentBpmnPropertyPanel` | `FluentTextInput` for id, `FluentAccordion` / `FluentAccordionItem` for groups, registered entries | None |
| `FluentBpmnPropertyForm`, `FluentBpmnPropertyEntryView` | Core property scope provider and dynamic registered editors | None |
| `FluentBpmnTextEntry`, `FluentBpmnTextAreaEntry` | `FluentTextInput`, `FluentTextArea`; drafts validated while typing, committed on blur | None |
| `FluentBpmnCheckboxEntry`, `FluentBpmnNumberEntry`, `FluentBpmnSelectEntry` | `FluentCheckbox`, `FluentNumberInput<double?>`, `FluentSelect<BpmnPropertyChoice, string>` | None |
| `FluentBpmnListEntry` | Native semantic table, `FluentTextInput`, `FluentButton` | `Add`, `Delete`, `ArrowUp`, `ArrowDown` |
| `FluentBpmnFeelEntry` | `FluentTextArea`, `FluentButton`; optional literal editor resolved from registry | None (`fx` is textual expression notation) |
| `FluentBpmnPropertyEntryFrame`, `FluentBpmnPropertyEntryHelp` | Native label/help/error relationships | None |
| `FluentBpmnTemplateChooser` | `FluentTextInput`, `FluentButton`, grouped native listbox | `Search`; template artwork follows Core's safe-icon policy |
| `FluentBpmnReferenceBrowser` | `FluentButton`, native semantic usage list | `ArrowRight` |
| `FluentBpmnContextMenu` | Native anchored menu, `FluentButton`, kit property form | None |
| `FluentBpmnContextualActions` | Native anchored toolbar, `FluentButton`, `FluentIcon` | `Delete`, `Edit`, `MoreHorizontal`; **append notation uses Core SVG silhouettes/glyphs** |
| `FluentBpmnValidationPanel` | `FluentButton`, semantic native diagnostic list | `ErrorCircle`, `Warning` |
| `FluentBpmnDiagramNavigator` | `FluentSelect<BpmnDiagramTarget, string>`, `FluentButton` | `ArrowRight` |
| `FluentBpmnShortcutHelp` | Inline `FluentDialog` via `FluentBpmnDialog`, `FluentButton` | `Dismiss` |
| `FluentBpmnImportOffer` | Inline `FluentDialog`, `FluentMessageBar` for parse errors, `FluentButton` | `Dismiss` |
| `FluentBpmnExternalChangeOffer` | Non-modal notice over the canvas, `FluentMessageBar`, `FluentButton` | `Dismiss` |
| `FluentBpmnCalledElementPicker` | Inline `FluentDialog`, `FluentButton`, semantic listbox options | None |

Native fallbacks are intentional: the stencil must expose DOM drag targets and Core keyboard
handlers; menus/action strips must use canvas client-coordinate anchors, not Fluent's
trigger-anchored menu/popover placement. Tables, labels, diagnostic lists and landmark wrappers
preserve standard kit semantics without adding grid-specific keyboard behavior. No copied BPMN
artwork or Fluent icon substitutes for BPMN notation.

## Composition and overrides

`FluentBpmnEditor` mirrors the standard editor's public parameters and imperative methods;
commands forward to `Session`. Slots: `ToolbarContent`, `StencilContent`, `PanelContent`,
`CanvasOverlays`, `Dialogs`, `Footer`. Instance slot content wins over registry configuration.

`AddFluentBpmnEditor(Action<BpmnOptions>? configure = null)` calls `AddBpmnComponents()` and
post-configures `FluentBpmnDefaults`. Every `BpmnEditorPart` has a default; Core's intrinsic
`BpmnLabelEditor` and `BpmnAccessibleTree` remain shared. `Use<T>` / explicit `Remove` and
property editor overrides win regardless of registration order. `AddBpmnFeelEditor()` replaces
this kit's default FEEL editor in either registration order.

## CSS and themes

`FluentBpmnEditor.css` is a plain static web asset, entirely scoped to `.fluent-bpmn-editor`.
It owns layout/chrome and preserves `.bpmn-editor__canvas` grid placement and anchored-overlay
hooks. It does not style unrelated standard editor instances.

The theme bridge maps Core's actual `--bpmn-*` tokens to Fluent v5's `--colorNeutral*`,
`--colorBrandBackground`, typography and severity tokens on **both root and canvas**. Canvas
styles declare tokens locally, so an ancestor-only bridge would not work. Fluent theme changes
therefore recolor both notation and chrome. The host must configure Fluent v5's theme service /
tokens for light/dark; the old v4 `FluentDesignTheme` component is not used.

Bridge selectors exclude `data-bpmn-contrast="high"`; forced-colors mode also bypasses the bridge.
Core's `data-bpmn-theme` / `data-bpmn-contrast` high-contrast palettes remain authoritative.
Explicit `ThemeOptions` custom tokens continue to apply inline to root and canvas.

## Localization

Kit-only strings use `Resources.FluentBpmnEditorStrings`; headless labels/messages use Core's
localized defaults. Empty `ResourcesPath` and the assembly `RootNamespace` attribute account
for the package/namespace difference. `EnableXlfLocalization=true` opts into XliffTasks.
All 13 languages ship satellites; untranslated XLIFF entries use `state="new"` and fall back
to English. Regenerate resources with:

```powershell
dotnet build src\FluentUI.BpmnEditor\Hasseware.FluentUI.AspNetCore.Components.BpmnEditor.csproj /t:UpdateXlf
```

Dynamic stencil, property, type-choice, menu-command, contextual-action and accessibility
captions use Core's semantic `BpmnDisplayLocalizer`, applied at source by the session and inspector;
document names, template captions and explicit host labels remain unchanged. Collection row actions and FEEL toggles use this kit's resources.

## Tests

`tests\Bpmn.FluentEditor.Tests` links Core's `TestScope.cs`, adds Fluent services and uses
loose JS interop for Fluent's web-component internals. Core canvas module assertions remain
explicit. Tests cover all registry parts, scope contracts, render/command behavior, input
read-only/draft/FEEL behavior, host overrides, API parity, package independence and zero kit
JavaScript-module owners. bUnit interacts with v5 web-component callbacks rather than assuming
native input elements exist outside their browser shadow roots.

Run the xUnit executable directly; this repository's `dotnet test` runner discovers no tests:

```powershell
& tests\Bpmn.FluentEditor.Tests\bin\Release\net10.0\Hasseware.FluentUI.AspNetCore.Components.BpmnEditor.Tests.exe
```

## Guides

- [Composing an editor](https://github.com/hasseware/BlazorBpmnEditor/blob/main/docs/composing-an-editor.md)
- [Writing a UI kit package](https://github.com/hasseware/BlazorBpmnEditor/blob/main/docs/writing-a-ui-kit.md)
- [Localization](https://github.com/hasseware/BlazorBpmnEditor/blob/main/docs/localization.md)

See the [2.0 migration notes](https://github.com/hasseware/BlazorBpmnEditor/blob/main/README.md#migrating-from-the-single-1x-package)
when upgrading from the single package.
