# Localization

The packages resource-back built-in UI text, including dynamic stencil/type/property captions,
canvas accessibility and gesture feedback, and validation diagnostic presentation. Core registration
`AddBpmnComponents()` calls `AddLocalization()` and registers `BpmnDiagnosticLocalizer`; both kit
registration extensions include Core registration. Consumers need no XLIFF build tool or Node.js.

## Resource ownership

| Package | Marker classes / English sources |
|---|---|
| Core | `Hasseware.AspNetCore.Components.Bpmn.Resources.BpmnStrings`, `BpmnRuleMessages` |
| Canvas UI | `Hasseware.AspNetCore.Components.Bpmn.Resources.BpmnCanvasStrings` |
| Standard kit | `Hasseware.AspNetCore.Components.Bpmn.Resources.BpmnEditorStrings` |
| Fluent kit | `Hasseware.FluentUI.AspNetCore.Components.Bpmn.Resources.FluentBpmnEditorStrings` |
| Demo client | `Bpmn.Demo.Client.Resources.DemoClientStrings` |
| Demo server | `Bpmn.Demo.Resources.DemoStrings` |

The playground also owns resources for its client pages and server layout/error surfaces.

Each marker has an adjacent `.resx`. Core owns text used by shared headless bases; kits own
markup-only strings. Use `IStringLocalizer<TMarker>` with an **empty `ResourcesPath`**, because
marker namespaces already include `Resources`. Both kits declare an assembly `RootNamespace`
attribute to handle their assembly-name/namespace differences. Explicit label parameters take
precedence over localized defaults where the component supports such defaults; host slot content
and document element names remain host/document text, not resource keys.

Built-in element-type captions are presentation text, not model identifiers. Localizing a stencil
entry, property option or accessible type name does not rename elements or change exported BPMN.
Template labels/descriptions and curated host labels retain their supplied text.

Built-in `BpmnPropertyChoice` captions carry `LabelResourceKey`; their `Value` remains the
binding/serialization value. Shared select controls resolve resource-keyed choices even outside
the built-in property panel. Unkeyed document/reference and host choices retain supplied labels;
legacy built-in descriptors also retain property-based localization for compatibility.

Built-in captions are localized where they are produced, not only when a kit draws them.
`BpmnElementInspector.Describe` localizes standard property labels, choice labels, schema group
captions and type-switch options; `BpmnEditorSession` localizes context-menu command labels and the
default contextual actions (`BpmnContextualActionsBase.DefaultsFor`). A session uses the host's
`IStringLocalizer<BpmnStrings>` from DI and re-describes elements when `CurrentUICulture` changes.
Direct `Describe`/`DefaultsFor` callers may pass a `BpmnDisplayLocalizer`; otherwise the built-in
resources are read for `CultureInfo.CurrentUICulture`, so neutral output stays English. Property
names, choice values, wire types, element ids, document labels and template captions are never
translated. Raw `BpmnElementInspector.TypeLabel`/`TriggerLabel` stay neutral for diagnostics and
history descriptions. Render-time `BpmnDisplayLocalizer` helpers translate only exact built-in English
captions, so already localized or host-supplied captions pass through unchanged.

## Source and translation workflow

`EnableXlfLocalization=true` in a library project opts into `src\Directory.Build.targets`, which references
`Microsoft.DotNet.XliffTasks` with `PrivateAssets="all"`. Central package management pins the version
in `Directory.Packages.props`. `NuGet.config` maps this build-only package to the public **dotnet-eng**
feed: `https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-eng/nuget/v3/index.json`.
Source builds/CI must reach this feed; other dependencies resolve from nuget.org.

1. Edit the neutral English `.resx`, keeping stable keys and formatting placeholders.
2. Regenerate XLIFF from the repository root:

   ```powershell
   dotnet build src\Bpmn\Hasseware.AspNetCore.Components.Bpmn.csproj /t:UpdateXlf
   dotnet build src\BpmnEditor\Hasseware.AspNetCore.Components.BpmnEditor.csproj /t:UpdateXlf
   dotnet build src\FluentUI.BpmnEditor\Hasseware.FluentUI.AspNetCore.Components.BpmnEditor.csproj /t:UpdateXlf
   dotnet build playground\Bpmn.Demo.Client\Bpmn.Demo.Client.csproj /t:UpdateXlf
   dotnet build playground\Bpmn.Demo\Bpmn.Demo.csproj /t:UpdateXlf
   ```

3. Translate `<target>` in `Resources\xlf\*.xlf`, preserving keys and `{0}`, `{1}`, ... placeholders.
   Set completed targets to `state="translated"`; newly generated English targets use `state="new"`.
4. Commit the `.resx` and XLIFF changes together. Build normally to validate synchronization and
   generate localized resources/satellite assemblies. Do not edit generated localized `.resx`
   files under `obj` or generated satellite assemblies.

Normal builds fail when XLIFF is stale. XliffTasks supplies its default 13 languages:
`cs`, `de`, `es`, `fr`, `it`, `ja`, `ko`, `pl`, `pt-BR`, `ru`, `tr`, `zh-Hans`, `zh-Hant`.
Satellites are packaged for these cultures and every target is translated (`state="translated"`).
The translations are machine-generated and have not had native-speaker review; improve them via
pull requests to the `.xlf` files. Newly added keys start as English `state="new"` until translated. Missing resources follow normal culture/parent/neutral fallback.

Demo projects opt into the same build-only tooling through `playground\Directory.Build.targets`.

## Host cultures

Resource lookup follows `CultureInfo.CurrentUICulture`; general formatting follows the host's
culture policy. Configure supported cultures and `UseRequestLocalization` on the server before
rendering the editor. For interactive WebAssembly, set culture at client startup as well; server
request localization alone does not configure the browser runtime. Preserve the same culture
through prerendering and hydration. Register editor services in both server and client containers.

For client applications supporting all listed cultures, enable full globalization data in the
client project (the demo does this):

```xml
<BlazorWebAssemblyLoadAllGlobalizationData>true</BlazorWebAssemblyLoadAllGlobalizationData>
```

Changing culture must also cause the editor to rerender; a culture selector typically persists
its choice and reloads the application. The library does not choose request cultures, set browser
preferences or translate user-authored BPMN names. Canvas UI receives resource-derived text from
Blazor; invariant rule identifiers and diagnostic/log payloads remain unchanged.
See [Blazor globalization and localization](https://learn.microsoft.com/aspnet/core/blazor/globalization-localization)
for host culture configuration.

## Validation messages

`BpmnDiagnostic.Message` retains its invariant English text for logs, export and rule parity.
For display, call `BpmnDiagnosticLocalizer.Localize(diagnostic)` from
`Hasseware.AspNetCore.Components.Bpmn.Validation`. It looks up `MessageResourceKey` or `RuleId`
in `BpmnRuleMessages`, formats `MessageArguments`, translates `DetailResourceKey` when available,
and preserves detail/profile text. Missing resources or malformed translated format strings fall
back to the original `Message`, so a translation cannot hide a finding.

The built-in validation/property/accessibility surfaces use this service. Custom diagnostic UI
should use it too rather than mutating diagnostics. New validator rules need matching resource
keys and tests; the Core suite checks rule-message coverage.

## Replace the localizer factory

Hosts may implement `IStringLocalizerFactory` to source text from their own resources, database
or translation service. After registering editor services, replace the factory (in every renderer
container that uses the editor):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

builder.Services.Replace(
    ServiceDescriptor.Singleton<IStringLocalizerFactory, MyStringLocalizerFactory>());
```

`MyStringLocalizerFactory` is the host's implementation of both `Create(Type)` and
`Create(string, string)`, returning an `IStringLocalizer`. Preserve resource namespaces/keys,
formatting arguments and `LocalizedString.ResourceNotFound` semantics. Use a singleton-safe
implementation; do not capture request-scoped dependencies in it. Core and kit marker localizers
resolve through the replacement factory. UI components also accept explicit text overrides where
appropriate, avoiding a global factory replacement for a single label.

See [Writing a UI kit package](writing-a-ui-kit.md) for adding localized kit resources and
[Composing an editor](composing-an-editor.md) for slot overrides and render-mode constraints.
