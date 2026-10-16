# Bpmn.Demo.Client

The WebAssembly client for the playground. This is the project that references the component
library, so its static web assets flow to the host transitively.

Run it through the host:

```shell
dotnet run --project playground/Bpmn.Demo --urls http://localhost:5400
```

## The pages are review surfaces, not a showcase

Each one exists to make a specific claim checkable by looking at it. They are as much a part of how
this library is verified as the test projects are.

| Page | What you can check on it |
|---|---|
| `/` | Home page and entry point to the playground |
| `/editor` | XML/JSON file preview, semantic model → interop → JointJS paper → command edits; sample chooser, zoom/pan/fit, properties, templates and export text |
| `/gallery` | The §4 notation matrix across five sheets — spec-valid event trigger/context cells, every gateway, every activity marker combination, connectors, swimlanes. Unsupported event cells are blank; non-interrupting starts belong to Event Sub-Processes, Boundary Events sit on real hosts, and white-box Pools own distinct Processes |
| `/icons` | Every glyph in the shared manifest, rendered through the **C#** registry. The review surface for a font bump or a regenerated manifest, and proof both languages read the same file |
| `/interchange` | Import → export → import in both modes, with per-measure comparison, export diagnostics and the XML each mode produced |

Try them in dark mode too: canvas notation must invert through `var(--bpmn-stroke)` rather than
baking a colour.

## Conventions

**Every component is `.razor` + `.razor.cs`.** No inline `@code` blocks, in the playground as much
as in the library.

The sample list is declared per page rather than discovered, so adding a fixture to
`playground/Bpmn.Demo/wwwroot/samples/` means adding its name to the pages that should offer it.

## Editor workflow

- **Open file** uses Blazor `InputFile`, `BpmnFileFormats.Accept` and a 64 MiB stream limit.
  XML or JSON content goes through `BpmnEditor.PreviewImport`: opening a picker does not
  immediately replace the current document. Canvas drops use the same offer/accept workflow.
- **Export XML** uses `BpmnWriteOptions.Fidelity`; documents opened as JSON have no XML source
  and fall back to canonical XML. **Export JSON** uses `BpmnJsonWriter.Write`. Both show generated
  text in the page's expandable export section; these buttons are not file-download endpoints.
- **Properties** toggles the panel. Its property and template callbacks feed the editor's command
  path, so edits participate in undo/redo instead of mutating a second document.
- The demo opts into the optional FEEL code-editor package with
  `AddBpmnComponents().AddBpmnFeelEditor()` in `Program.cs`. FEEL-capable property entries use that
  editor; this is demo configuration, not a dependency required by the core RCL.
- `Program.cs` registers Zeebe and Camunda 7 extension schemas, disables always-offered schemas,
  sets the Camunda engine context to `8.6.0`, and loads embedded `.templates.*.json` resources.
  Template choices, defaults, conditional fields, apply/unlink actions and template stencil items
  can therefore be exercised against real configuration.

The `/interchange` page remains the **XML** comparison surface: it compares fidelity and canonical
exports and offers explicit lossy-canonical consent. JSON file opening and text export live on
`/editor`. See [JSON interchange](../../docs/json-interchange.md) and
[Editing](../../docs/editing.md) for the underlying contracts.
