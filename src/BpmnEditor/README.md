# Hasseware.AspNetCore.Components.BpmnEditor

Standard HTML BPMN editor chrome for Blazor, depending only on the Core package.
No Fluent UI services, providers, JavaScript modules or packages are required.

## Install and register

```shell
dotnet add package Hasseware.AspNetCore.Components.BpmnEditor
```

```csharp
builder.Services.AddBpmnEditor();
```

```razor
@using Hasseware.AspNetCore.Components.Bpmn

<BpmnEditor Document="@document" />
```

Include the two standard stylesheets in your host HTML (Core's canvas bundle still loads through
its JS initializer):

```html
<link rel="stylesheet" href="_content/Hasseware.AspNetCore.Components.BpmnEditor/css/BpmnEditor.css" />
<link rel="stylesheet" href="_content/Hasseware.AspNetCore.Components.BpmnEditor/css/BpmnOverlays.css" />
```

## Composition and overrides

`BpmnEditor` preserves parameters and imperative methods, forwarding commands to its `Session`.
It wraps Core's non-visual `BpmnEditorScope` and `BpmnEditorCanvas`. All registry parts, including
Toolbar and PropertyPanel, have defaults. `ToolbarContent`, `StencilContent` and `PanelContent`
override defaults per instance; `CanvasOverlays`, `Dialogs` and `Footer` add content.

`AddBpmnEditor()` calls `AddBpmnComponents()` and post-configures only-if-unset defaults.
Host `Configure<BpmnOptions>` registrations win in either order, including explicit `Remove`.
Property editor overrides and optional `AddBpmnFeelEditor()` retain precedence.
`BpmnEditor` also passes the same defaults as its own kit defaults, which rank above registry defaults, so
it renders with standard parts even when another kit's `Add...()` shares the container.
Keep scope, canvas and parts inside one interactive render-mode boundary.

## Package boundaries and writing another kit

Concrete layout/chrome and property entries live here; components retain namespace
`Hasseware.AspNetCore.Components.Bpmn` for consumer compatibility. Core keeps every abstract
widget base, `BpmnPropertyEntryComponent`, session/state, typed contexts, intrinsic label editor,
accessible tree and the minimal read-only property fallback. Other kits depend on Core, not this
package. Use `BpmnPropertyScopeProvider` to create typed property cascades and
`BpmnGeneralPropertiesProvider.FromDescriptor` to adapt legacy descriptors. The property panel
base renders registered entries through `DynamicComponent`; it does not name standard types.

Core owns theme tokens and canvas, shape, accessibility, execution and label-editor CSS.
Standard owns layout, toolbar, stencil, picker, shortcut-help, diagram navigation and chrome
overlays (menus, contextual actions, properties, templates, usages, validation and import).

| Stylesheet | Owner |
|---|---|
| `BpmnCanvas.css`, `BpmnShapes.css`, `BpmnAccessibility.css`, `BpmnExecution.css`, `BpmnTheme.css`, `BpmnLabelEditor.css` | Core, assembled by esbuild into its existing canvas bundle |
| `BpmnEditor.css`, `BpmnOverlays.css` | Standard static web assets at the paths above |

Core `BpmnEditorCanvas` emits a `.bpmn-editor__canvas` wrapper; another kit must style its
layout placement too.

Theme selectors support `.bpmn-editor`, `.bpmn-editor-scope`, `.fluent-bpmn-editor` and standalone
`.bpmn-canvas`; palette attributes and custom tokens must also reach the canvas itself.

## Localization

Kit-only markup strings use `Resources.BpmnEditorStrings`; strings consumed by headless bases
stay in Core's `BpmnStrings`. Localization uses empty `ResourcesPath`. Because the package's
assembly name differs from its component namespace, the assembly declares `RootNamespace`.
`EnableXlfLocalization=true` opts into XliffTasks; all 13 default languages ship as satellites.
Regenerate committed XLIFF after editing resources:

```shell
dotnet build src\BpmnEditor\Hasseware.AspNetCore.Components.BpmnEditor.csproj /t:UpdateXlf
```

Untranslated entries fall back to English. Hosts can replace the string-localizer factory.

Dynamic stencil, property, type-choice, menu-command, contextual-action and accessibility
captions use Core's semantic `BpmnDisplayLocalizer`, applied at source by the session and inspector;
document names, template captions and explicit host labels remain unchanged. Collection row actions and FEEL toggles use this kit's resources.

## Tests

`tests/Bpmn.Editor.Tests` mirrors Core's xUnit executable runner. Chrome/composition/entry tests
moved here; Core retains model/session/intrinsic tests. `TestScope.cs` is linked into both test
projects with a standard-only partial registration hook. Package-boundary tests forbid Fluent
references and JavaScript module ownership.

## Guides

- [Composing an editor](https://github.com/hasseware/BlazorBpmnEditor/blob/main/docs/composing-an-editor.md)
- [Writing a UI kit package](https://github.com/hasseware/BlazorBpmnEditor/blob/main/docs/writing-a-ui-kit.md)
- [Localization](https://github.com/hasseware/BlazorBpmnEditor/blob/main/docs/localization.md)

See the [2.0 migration notes](https://github.com/hasseware/BlazorBpmnEditor/blob/main/README.md#migrating-from-the-single-1x-package)
when upgrading from the single package.
