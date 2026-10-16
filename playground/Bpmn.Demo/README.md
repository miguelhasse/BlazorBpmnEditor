# Bpmn.Demo

The host application for the playground: a Blazor Web App that serves the WebAssembly client in
`Bpmn.Demo.Client`, plus the sample BPMN documents everything else reads.

```shell
dotnet run --project playground/Bpmn.Demo --urls http://localhost:5400
```

Pick a port explicitly. A forgotten host from an earlier run will happily answer on the default one
and serve you a stale build.

## What it contributes

- **The sample corpus**, in `wwwroot/samples/`. Twelve are real exports from real modelling tools —
  they use optional attributes, element orderings and vendor extensions that hand-written test XML
  never does. Two (`vendor-extensions.bpmn`, `global-catalog.bpmn`) are hand-built to pack in the
  interchange constructs the real ones only show one at a time. There is one copy of each: the unit
  tests, the E2E tests and this app all read the same files.
- **The demo styles**, in `wwwroot/css/app.css`. Note `.demo-editor` gets an explicit height — a
  zero-height flex parent is the usual reason a JointJS paper renders but shows nothing.
- **The layout and error UI**, in `Components/`.

## Also the verification surface

This app is what `Bpmn.E2E.Tests` launches, and what the repo's `/verify` skill drives with a
headless browser. The pages themselves live in `Bpmn.Demo.Client`; see that project's README for
what each one is for.

Warnings-as-errors is off for the playground projects, to keep FluentUI's analyzer noise out of the
build.

## Routes and demo features

The host serves these client pages (source: `Bpmn.Demo.Client\Pages`):

| Route | Purpose |
|---|---|
| `/` | Playground home |
| `/editor` | Sample/file editing, properties, template stencil, XML and JSON text export |
| `/gallery` | Notation review across gallery sheets |
| `/icons` | Shared icon-manifest review |
| `/interchange` | XML canonical/fidelity comparison and loss diagnostics |

Host components additionally provide `/Error` and `/not-found` pages. Application demonstrations
are implemented in the WebAssembly client, not duplicated in those server pages.

On `/editor`, **Open file** is an `InputFile` picker for XML/JSON and creates an import offer before
replacement. **Export XML** requests fidelity XML; **Export JSON** emits the versioned model
format. Both display generated text. The optional properties panel demonstrates undoable property,
extension and template edits. The client explicitly registers `AddBpmnFeelEditor()` and embedded
element templates, plus Zeebe/Camunda 7 extension schemas.

See [client setup and workflows](../Bpmn.Demo.Client/README.md) for opt-in details and
[JSON interchange](../../docs/json-interchange.md) for compatibility and source-fidelity limits.
