# Trimming and Native AOT

Native AOT support is a **feature-and-host contract**, not an unconditional claim for
every assembly in this repository. A successful managed build, or a native application
that never calls an unsupported feature, does not certify the entire package.

.NET 10 Blazor Server does not support Native AOT. Blazor WebAssembly trimming and
WebAssembly AOT are different deployment models; neither makes a Blazor Server host
Native AOT compatible. See Microsoft's
[ASP.NET Core Native AOT support table](https://learn.microsoft.com/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0).

## Current qualification

The native verification hosts target .NET 10, disable reflection-based JSON, treat
warnings as errors, and execute their published artifacts. The paths below have been
published and executed on Windows x64. CI repeats the supported probes on Linux x64;
a Linux qualification depends on that job passing.

| Package / feature | Evidence and boundary |
|---|---|
| `Bpmn` headless model | Native XML/JSON import/export, enum wire names including unknown values, canvas JSON projection, generated configuration/viewport/execution JSON and nested callback decoding, validation/editing, embedded icons and vendor schemas, and template loading/registry lookup. This does **not** qualify native server-side component rendering. |
| `Bpmn.Tools` | Native diagram retrieval, validation and generated tool-result JSON. Arbitrary host-defined result payloads still require their own generated contracts. |
| `Bpmn.Storage.FileSystem` | Native save/load and stale-revision conflict. |
| `Bpmn.Storage.ServerSentEvents` and `.Client` | Native HTTP endpoint, ready/change messages, missing/duplicate query paths, and subscriber delivery. Authentication/tenant behavior remains covered by managed conformance tests, not by the anonymous loopback native host. |
| `Bpmn.Storage.SignalR` and `.Client` | Native loopback connection, subscription and change delivery. Both sides register generated string payload metadata; BPMN hub arguments/results are strings, with change envelopes serialized separately. This does not qualify every SignalR transport, custom hub or serializer. |
| `Bpmn.Mcp` and `Bpmn.Mcp.AspNetCore` | Native HTTP tool discovery and invocation probe. No claim for arbitrary host tools, which must supply the MCP SDK's generated metadata. |
| `Bpmn.Mcp.Tool` | Native validation JSON plus stdio initialization, tool discovery and invocation. Native executable publishing is separate from distribution as a managed .NET tool. |
| `Bpmn.Scripting.JavaScript` | Native valid/invalid syntax parsing. Scripts are not executed. |
| `Bpmn.Scripting.Lua` | Native valid/invalid syntax parsing. Scripts are not executed. |
| `Bpmn.Scripting.CSharp` | **Dependency blocked:** publishing the syntax-parser probe reaches Roslyn `CommonCompiler.GetAssemblyLocation` and reports IL3000 (`Assembly.Location` in a single-file application). No warning suppression or runtime script compilation is introduced. |
| `Bpmn.FeelSharp` | **Dependency blocked:** the restricted interpreter still reaches FeelSharp service discovery and FSharp.Core reflection/formatting code during native publishing (including IL2026, IL2070, IL2072 and IL2075). Disabling engine auto-discovery is not sufficient proof of trimming safety. Result JSON is reflection-free for FEEL nulls, booleans, numbers, strings, contexts, lists and the engine's textual temporal/range/function representations. |
| `FluentUI.Bpmn.FeelSharp` | Inherits the FeelSharp boundary. Runtime `EditorType` field/workspace customization remains a `DynamicComponent` trimming boundary (IL2111); it is not certified by the headless probes. |
| `BpmnEditor`, `FluentUI.BpmnEditor` | Component registration carries member-preservation requirements through registry storage, merges and rendering. Published, linked WebAssembly probes exercise both default kits and a custom editor's parameters, injection, callbacks and read-only state through both registry views. This is not a blanket qualification of every component or host. |
| `Bpmn.CamundaFeel` | Browser editor assets, not a native execution engine. Fallback component types use preservation-aware rendering; linked-browser qualification remains separate. |
| `Bpmn.WebMcp` | Published, linked WebAssembly tests exercise browser registration and tool invocation through a stub of the browser's model-context API. This qualifies the owned module/interop path, not availability or behavior of a browser's experimental WebMCP implementation. |
| `Bpmn.Storage.AzureBlob` | Owned code analyzed; native Azure SDK network operations are **not qualified**. Managed tests/fakes do not establish native network compatibility. |
| `Bpmn.Storage.EfCore` | DI type preservation is explicit. Native deployment still depends on the chosen provider, compiled model and generated queries; arbitrary host DbContexts and runtime LINQ are **not qualified**. |
| `Bpmn.RulesGenerator` | Build-time-only, not a runtime Native AOT dependency. It remains `netstandard2.0` and ignores application publishing flags. |
| `*.Scripts` projects | Build-time JavaScript assets; .NET Native AOT does not apply. |

No global `IsAotCompatible` declaration, whole-assembly linker roots or blanket IL
warning suppressions are used to conceal the remaining boundaries.

## Repeatable verification

Use the .NET 10 SDK and the platform's native compiler/linker prerequisites. Windows
needs Visual Studio C++ build tools; Linux needs clang and zlib development headers.
The canvas build also needs Node.js/npm, as for any source build.

```powershell
dotnet publish tests\Bpmn.AotSmoke\Bpmn.AotSmoke.csproj -c Release -r win-x64
& tests\Bpmn.AotSmoke\bin\Release\net10.0\win-x64\publish\Bpmn.AotSmoke.exe

dotnet publish tests\Bpmn.AotHttpSmoke\Bpmn.AotHttpSmoke.csproj -c Release -r win-x64 -p:BpmnSmokeMcp=true
& tests\Bpmn.AotHttpSmoke\bin\Release\net10.0\win-x64\publish\Bpmn.AotHttpSmoke.exe

dotnet publish tests\Bpmn.AotHttpSmoke\Bpmn.AotHttpSmoke.csproj -c Release -r win-x64 -p:BpmnSmokeSignalR=true
& tests\Bpmn.AotHttpSmoke\bin\Release\net10.0\win-x64\publish\Bpmn.AotHttpSmoke.exe
```

The HTTP host binds an ephemeral loopback port, makes its own requests and shuts
down. It is not a sample production security configuration. Its anonymous,
single-tenant settings are intentional test settings.

Set `BpmnSmokeParsers=javascript` or `BpmnSmokeParsers=lua` on the console host
to include the corresponding parser. `BpmnSmokeParsers=csharp` (or `true` for
all three) and `BpmnSmokeFeel=true` are reproducible probes for the dependency
blockers above; they are not passing compatibility gates.

The CLI can also be published with `PublishAot=true`, `PackAsTool=false` and
`JsonSerializerIsReflectionEnabledByDefault=false`. Run
`tests\Bpmn.AotSmoke\Verify-NativeCli.ps1` with `-Executable` pointing to the
published binary and `-SampleDirectory` pointing to
`playground\Bpmn.Demo\wwwroot\samples`. The script checks validation JSON and the
real stdio protocol; it never enables write tools.

For complete owned-source analysis, rather than only the reachable native paths:

```powershell
dotnet build BlazorBpmnEditor.slnx -p:BpmnCompatibilityAnalysis=true
```

This opt-in enables trimming/AOT analyzers on shipping C# projects, excluding the
build-time generator. It intentionally reports unresolved boundaries, including the
FEEL field's runtime component-type parameter. It is **not** currently a warning-free
whole-solution compatibility certification. Normal JIT builds retain their behavior.

CI also runs `tests\Bpmn.AotSmoke\Verify-CompatibilityAnalysis.ps1`. It rebuilds
every owned source project and rejects new IL diagnostics, allowing only the exact
documented `BpmnFeelSharpField`/`DynamicComponent.Type` IL2111 boundary. This is a
visible, narrowly matched known failure, not a linker suppression. The script does
not inspect third-party method bodies; native publishing performs that analysis.

Analysis enables `IsTrimmable` in build outputs. Rebuild without
`BpmnCompatibilityAnalysis` before using those outputs for normal deployment or
`--no-build` packaging; CI does this explicitly after its analysis gate.

### Linked browser verification

The existing playground doubles as the verification host, avoiding a second copy of
its editor, FEEL, localization and WebMCP setup. Publishing explicitly sets `TrimMode=full`
to trim application/library code, not just the SDK's default partial-trimming surface.
The E2E fixture then starts the published server in Production and serves those
assets rather than development output. The host itself remains a managed ASP.NET Core
application, not a Native AOT Blazor Server host.

Interactive WebAssembly entry points named by server-generated descriptors need host-level
preservation. The demo's `TrimmerRoots.xml` lists only its eight page entry points; it does
not root a library, an assembly, or the custom editor used by the registry probe. Hosts
using equivalent dynamic entry points must preserve their own page types.

```powershell
dotnet build tests\Bpmn.E2E.Tests -c Release
dotnet publish playground\Bpmn.Demo\Bpmn.Demo.csproj -c Release -p:TrimMode=full -o artifacts\publish-demo
$env:BPMN_E2E_PUBLISHED_DIRECTORY = (Resolve-Path artifacts\publish-demo).Path
try {
    & tests\Bpmn.E2E.Tests\bin\Release\net10.0\Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.exe `
        -class '*CompatibilityTests' -class '*CanvasSmokeTests' `
        -class '*FeelAuthoringTests' -class '*WebMcpTests' `
        -class '*ExecutionOverlayTests' -maxThreads 2 -noColor
    if ($LASTEXITCODE -ne 0) { throw 'Linked browser verification failed.' }
}
finally {
    Remove-Item Env:\BPMN_E2E_PUBLISHED_DIRECTORY
}
```

These probes passed on Windows with Chromium, including default rendering in both kits,
custom registry merging, reflected component parameters/injection, edit callbacks,
read-only propagation, FEEL apply/cancel/evaluation authorization and WebMCP results.
Execution probes also verify status/token/incident overlays, animation, plane switches,
document changes and configuration rebuilds against the fully linked browser assets.
The installed FSharp.Core dependency emits IL2040 missing-resource substitution warnings
during WebAssembly linking; these are not suppressed and do not certify its full surface.
CI repeats this focused published-output gate after publishing the playground.

## Component and serializer obligations

Register custom parts/editors through the generic `Use<TComponent>`,
`AddEditor<TComponent>` and `AddFeelEditor<TComponent>` APIs. Their preservation
annotations follow the component type through registry copies into
`RenderTreeBuilder.OpenComponent`. This preserves registered component members,
not every type in an assembly. Forwarding generic APIs must propagate the same
`DynamicallyAccessedMembers` requirement.

An arbitrary runtime `Type` supplied to a FEEL field's `EditorType` is not equivalent
to a typed registration. That legacy customization path remains unqualified for
trimming; do not suppress its warning and infer safety.

Use generated `JsonTypeInfo`/`JsonSerializerContext` contracts for host-owned JSON
payloads. Persisted document JSON and the canvas projection deliberately use
different generated contexts: the canvas retains explicit nulls and derived colors
but omits C#-only document metadata. Reusing the persisted format's options there
would change the browser wire contract.

Canvas configuration, viewport and execution payloads cross JS interop as generated
`JsonElement` values. Canvas callbacks accept `JsonElement` and decode with generated
metadata before forwarding to the existing typed methods. Explicit `JSInvokable`
identifiers retain the original JavaScript method names. Typed canvas result DTOs
also have generated metadata so linking retains their constructors. WebMCP likewise
uses generated descriptor and registration-result contracts rather than reflection.
No application-wide serializer resolver or framework serializer replacement is needed.

Headless storage/tool hosts do not need editor component defaults. `BpmnOptions`
initializes those defaults lazily on first access to `Components`, so resolving
storage binding options no longer roots Blazor rendering into an otherwise
headless native executable.
