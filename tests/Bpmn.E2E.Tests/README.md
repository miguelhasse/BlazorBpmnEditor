# Bpmn.E2E.Tests

Browser tests that drive the real playground application with Playwright.

From the repository root, build first so the Playwright install script exists, then install
Chromium and run the executable test project:

```powershell
dotnet build tests\Bpmn.E2E.Tests
pwsh tests\Bpmn.E2E.Tests\bin\Debug\net10.0\playwright.ps1 install chromium
dotnet run --project tests\Bpmn.E2E.Tests
```

Browser installation is needed once per Playwright version. On hosts needing browser system
packages, use Playwright's `install --with-deps` option. Unit-only runs need neither the demo nor
Playwright; see [Bpmn.Tests](../Bpmn.Tests/README.md).

## Why these are separate from the unit tests

The canvas only exists once the WebAssembly client has booted and imported the real static web
assets. `DemoAppFixture` therefore launches `playground/Bpmn.Demo` as a **separate process on a
free port** and polls until it answers, rather than hosting it in-process — starting it any other
way would skip the part these tests are here to check.

It also means assertions about a real `dia.Paper` belong here. JointJS's ESM circular imports break
paper construction under Vite's SSR transform, so the Vitest suite in `src/Bpmn.Scripts/test` keeps
to pure logic and everything paper-dependent lands in this project.

## What is covered

| File | What it drives |
|---|---|
| `CanvasSmokeTests.cs` | Navigate to `/editor`, pick a sample, assert the paper exists with the expected cell count, exercise zoom and fit, and require zero `console.error` |
| `RenderDiagnosticsTests.cs` | The import-diagnostic surface for documents that do not read cleanly |
| `GalleryTests.cs` | Spec-valid event cells, real Boundary hosts and Event Sub-Process starts, both Pool orientations, unique accessible items and repeated sheet switching without rendering errors |
| `Fixtures/DemoAppFixture.cs` | Process lifecycle: free port, launch, poll until ready, kill on dispose |

## Gotchas

A demo host left running from an earlier session holds `bin/` locks and the next build fails with
MSB3027. Stop only the specific host process you started before rebuilding; do not terminate unrelated
`dotnet` processes on a shared machine. The repo's `/verify` skill documents the
same recipe for manual runs.

`DemoAppFixture` launches with `--no-build` and explicitly matches the test assembly
configuration (Debug or Release). The test project has a build-only reference to the demo, so
normal `dotnet run` builds both in that configuration. When invoking a built runner directly,
rebuild first or you can still test stale output. The TypeScript bundle is worse: there is
one copy of it, and its build mode is whichever configuration was built **last**, not which host is
running. A few tests need the Debug-only `__moveElement` hook and skip with a stated reason when the
loaded bundle is a Release build, rather than passing without asserting anything.

## Editing and host integration coverage

Beyond smoke rendering, the suite includes:

| Tests | Behavior |
|---|---|
| `ImportTests` | Offer without replacement, cancel, accept, parse failures and document counts |
| `PropertyPanelTests` | Selection metadata, label/documentation edits, atomic bulk undo and preserved extension edits |
| `TemplatePanelTests` | Template stencil creation, applying, editing, undo, unlink/remove and export |
| `EditingTests`, `ClipboardAndResizeTests`, `ConnectorEditTests` | Command-backed canvas interactions |
| `RoutingTests`, `CanvasNavigationTests`, `KeyboardNavigationTests` | Real-paper geometry and navigation |
| `DiagramNavigationTests`, `ReferenceBrowserTests` | Cross-diagram and reference navigation |
| `ValidationTests`, `ExecutionOverlayTests` | Findings and non-editing runtime overlays |

Diagram navigation coverage includes picker changes synthesizing a root-to-selected host breadcrumb,
retaining clickable ancestor steps, and hiding the breadcrumb when only the root remains.

`DemoAppFixture` shares one demo host across test classes, protected by a gate and reference count;
individual tests own their browser pages. The last fixture user disposes the process. This avoids
starting a host per parallel class while preserving document isolation in the browser.

## Both kits

The demo serves `/editor` (Fluent kit) and `/editor-plain` (standard kit). Shared test classes are
non-sealed and navigate with `app.Url(this)` (`KitRoute`); `PlainKitTests.cs` holds `PlainXxxTests`
subclasses that inherit every test and run it against `/editor-plain`. Locators that cross kits use
roles and accessible names (`KitRoute.Textbox`, `PickAsync`, `ShortcutDialog`) rather than kit
markup. `GalleryTests`, `TemplatePanelTests` and the geometry suites run on one kit (Fluent, or
Plain for the template chooser). Viewport-sensitive tests set their own size: the canvas only
renders what is on screen, so a default 1280x720 page can leave a target off-canvas.

To focus a runner invocation, for example:

```powershell
dotnet run --project tests\Bpmn.E2E.Tests -- -class "*TemplatePanelTests"
```

JSON serializer unit coverage is in `Bpmn.Tests\Serialization\BpmnJsonTests.cs`; the import
browser tests should not be read as a claim that every JSON failure case has browser coverage.

On CPU/memory-constrained machines, bound browser concurrency without filtering tests. For example,
after a full Release build, run the built xUnit executable directly:

```powershell
.\tests\Bpmn.E2E.Tests\bin\Release\net10.0\Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.exe -maxThreads 4 -noColor
```

This still runs the complete suite against both kits. Excessive parallel Chromium instances can
starve initial WebAssembly/sample loading and trigger startup timeouts unrelated to editor state.
The six Release-only move-hook skips remain expected; no additional skips are introduced by this limit.

The fixture continuously drains redirected host output and retains a bounded stderr startup tail.
An undrained redirected console can block host logging once its pipe fills, stalling requests
and causing misleading initial-page timeouts during long browser runs.
