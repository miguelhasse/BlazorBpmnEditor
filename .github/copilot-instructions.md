# Copilot Instructions

## Architecture

- `src\Bpmn` is the framework/core layer: BPMN semantic and DI models, editing state and history, validation, XML/JSON interchange, canvas component, and intrinsic overlays. `src\BpmnEditor` and `src\FluentUI.BpmnEditor` provide separate standard-HTML and Fluent UI chrome; neither kit depends on the other. Keep behavior in Core/base components and kit-specific rendering/defaults in the corresponding kit.
- The canvas is a view, not persisted state. The TypeScript module in `src\Bpmn.Scripts` renders and handles SVG/JointJS interactions, then sends edit requests to Blazor. In Core, `BpmnCanvas` owns the JS object reference and `BpmnCanvasEventBridge` owns `[JSInvokable]` methods; architecture tests enforce this boundary. Edit authored component scripts in their `.ts` sources, not generated `.js` bundles.
- Storage backends, live-update transports, FEEL editors, and agent/MCP surfaces are optional packages layered on Core. `Bpmn.Tools` supplies handlers shared by WebMCP and server MCP. Preserve package boundaries and avoid adding optional dependencies to Core.
- `rules/`, `icons/`, and `playground/Bpmn.Demo/wwwroot/samples/` are shared single sources consumed by code, browser rendering, and tests. Update the source artifact and its verification/tests rather than creating duplicate copies.

## Project Guidelines

- In this repository, component script changes must be made in the corresponding .ts file, not generated .js files.
- Before debugging UI issues with Playwright, verify the Playwright connection first; if it is unavailable, close the browser or tab and retry before proceeding.

## Repository-specific invariants

- Keep BPMN semantics and BPMNDI geometry distinct. The canvas must not become the source of truth; do not invent missing geometry or silently discard unknown vendor content. Preserve exact imported geometry and report losses or normalizations during interchange.
- Editor parts rely on typed `BpmnEditorSession`, `BpmnEditorState`, `BpmnCanvasSurface`, and `BpmnPropertyScope` cascades and required parent scopes. Keep a composition in one interactive component because cascades and fragment slots do not cross render-mode boundaries.
- Standard and Fluent kits share component-contract and parity tests under `tests\Shared`. Keep public component behavior, localization, and accessibility aligned unless a kit-specific difference is intentional.
- Connection rules are shared between C# and TypeScript through the root `rules/` data and `src\Bpmn.RulesGenerator`; change the shared source, not a generated copy.

## Skills

- When writing, reviewing, or refactoring C# / .NET / Blazor code, or when authoring unit tests with xUnit and bUnit, load and follow the [csharp-naming-conventions](skills/csharp-naming-conventions/SKILL.md) skill. Read the `SKILL.md` file before producing code so the conventions, layout rules, and testing guidelines are applied.

## Browser Automation Guidelines

- When browser automation initially fails in this workspace, retry Playwright actions instead of assuming the browser backend is unavailable.

## Build, test, and lint

- Requires .NET 10 and Node.js 22. Restore and build from the repository root with `dotnet restore BlazorBpmnEditor.slnx` and `dotnet build BlazorBpmnEditor.slnx -c Release`.
- Verify C# formatting with `dotnet format BlazorBpmnEditor.slnx --verify-no-changes --no-restore`.
- For the canvas TypeScript, run `npm ci` and `npm run check` from `src\Bpmn.Scripts`. `check` runs type-checking, ESLint, icon verification, and Vitest. For WebMCP scripts, run the same commands from `src\Bpmn.WebMcp.Scripts`.
- For the FeelSharp DOM bridge, run `npm ci` and `npm test` from `src\Bpmn.FeelSharp.Scripts`. The optional Camunda FEEL editor scripts use `npm ci` and `npm run build` in `src\Bpmn.CamundaFeel.Scripts`.
- Core tests use the xUnit v3 executable runner. Run all with `dotnet run --project tests\Bpmn.Tests`; run one test method with `dotnet run --project tests\Bpmn.Tests -- -method "*FullyQualifiedTestClass.TestMethod"`. Use `-class "*TestClass"` to focus a class. `dotnet test` can report zero tests for these executable-runner suites.
- Browser tests require Chromium. Build with `dotnet build tests\Bpmn.E2E.Tests`, install it with `pwsh tests\Bpmn.E2E.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`, then run `dotnet run --project tests\Bpmn.E2E.Tests`. Focus a class or method with `-- -class "*TestClass"` or `-- -method "*FullyQualifiedTestClass.TestMethod"`.

## Communication Rules

Terse like caveman. Technical substance exact. Only fluff die.
Drop: articles, filler (just/really/basically), pleasantries, hedging.
Fragments OK. Short synonyms. Code unchanged.
Pattern: [thing] [action] [reason]. [next step].
ACTIVE EVERY RESPONSE. No revert after many turns. No filler drift.
Code/commits/PRs: normal. Off: "stop caveman" / "normal mode".
