# Bpmn.Tests

Unit and architecture tests for `Hasseware.AspNetCore.Components.Bpmn`. xUnit v3, run as an
executable:

```shell
dotnet run --project tests\Bpmn.Tests
# After a Debug build, invoke the runner directly on Windows:
.\tests\Bpmn.Tests\bin\Debug\net10.0\Hasseware.AspNetCore.Components.Bpmn.Tests.exe
```

`InternalsVisibleTo` is granted from the library, so internal seams can be asserted directly.

## What lives where

| Folder | What it proves |
|---|---|
| `Architecture/` | The interop boundary holds — mechanically, not by convention |
| `Components/` | bUnit render and lifecycle tests with `JSRuntimeMode.Strict` |
| `Serialization/` | XML canonical/fidelity output, JSON envelopes/limits/null rejection and cross-format round trips |
| `Editing/` | Atomic commands, history, containment, extension editing and element-template binding changes |
| `Validation/` | Core rules, profiles, extension safety and managed FEEL/JUEL syntax checks |
| `Model/` | Extension schemas, template loading and template-icon policy |
| `Accessibility/`, `Configuration/`, `Geometry/` | Model-based navigation, option contracts and connector endpoint normalization |
| `Icons/` | The C# icon registry agrees with the manifest the canvas bundles |

## The architecture tests are the load-bearing ones

`Architecture/InteropOwnershipTests.cs` scans the library's own source text and reflection surface
to assert that only `BpmnCanvas` declares an `IJSObjectReference`, only `BpmnCanvasEventBridge`
declares `[JSInvokable]` methods, nothing else mentions `DotNetObjectReference`, and every bridge
method has a matching entry in the TypeScript event bridge.

These exist because that boundary is exactly the kind that erodes through one reasonable-looking
call at a time, and no reviewer catches the fourth one.

## Round-trip tests

`Serialization/RoundTripTests.cs` reads each of the fourteen sample documents, writes it in both
export modes, reads the result back, and asserts that semantic content, DI geometry, references and
preserved vendor content all survive — plus that a second write produces identical bytes.

Equality is asserted semantically rather than byte for byte: two serializers can legitimately
disagree about apostrophe escaping while meaning the same thing, and a test that failed on that
would be measuring the XML writer rather than the round trip.

The fixtures live in `playground/Bpmn.Demo/wwwroot/samples/` — one copy, so the demo can render the
same files these tests assert on.

## bUnit notes

bUnit 2.x: the type is `BunitContext`, rendering is `Render(...)`, and a module interop handle must
be cached in a field, because each `SetupModule` call returns a fresh one and comparing against a
second handle silently never matches.

Anything needing a real JointJS paper belongs in `Bpmn.E2E.Tests`, not here.

## Runner and focused coverage

The executable is the xUnit v3 in-process runner. `--help` prints runner options; `dotnet run`
builds before launching, whereas direct executable invocation uses whatever was last built.
For a focused run, pass runner arguments after `--`:

```powershell
dotnet run --project tests\Bpmn.Tests -- -class "*BpmnJsonTests"
dotnet run --project tests\Bpmn.Tests -- -class "*BpmnExpressionValidatorTests"
```

`BpmnJsonTests` checks the versioned envelope, model preservation, input limits and malformed
collection entries. `ExpressionEntryTests` checks property-entry warnings; `FeelCodeEditorTests`
checks the optional FEEL component. `Editing\Templates` covers Camunda 7 binding handling,
application, upgrades and registry integration. Pure managed syntax checks do not require a
browser; real code-editor/canvas behavior belongs in the E2E project.

## Kit boundaries and parity

`Bpmn.Editor.Tests` and `Bpmn.FluentEditor.Tests` link `tests\Shared\ComponentContractTests.cs`
and `KitParityTests.cs`. Reflection discovers all public components and registry defaults, enforcing
parent scopes, base types and parameterless rendering. The same parity assertions cover accessible
names, Core localization, toolbar history/zoom, stencil creation, property commits, context menus,
contextual append and read-only mode. Only the kit fixture differs; Fluent uses loose JS interop and
host-owned `AddFluentUIComponents()` registration. In bUnit, Fluent buttons are custom-element hosts;
real shadow-DOM accessibility remains covered by browser tests.

`Bpmn.FluentEditor.Tests\Components\PackageArchitectureTests.cs` checks all four package dependency
graphs, kit/FEEL JS ownership and public-context hygiene. Core interop ownership remains here.
`Localization\XliffCoverageTests.cs` discovers every resource in all three editor packages and both demo projects and checks
all 13 XLIFF catalogs for matching keys and source text. Diagnostic rule localization retains its
existing catalog/native/profile coverage.

After Release builds, run each xUnit executable directly (not `dotnet test`, which can report zero tests):

```powershell
.\tests\Bpmn.Tests\bin\Release\net10.0\Hasseware.AspNetCore.Components.Bpmn.Tests.exe
.\tests\Bpmn.Editor.Tests\bin\Release\net10.0\Hasseware.AspNetCore.Components.BpmnEditor.Tests.exe
.\tests\Bpmn.FluentEditor.Tests\bin\Release\net10.0\Hasseware.FluentUI.AspNetCore.Components.BpmnEditor.Tests.exe
```
