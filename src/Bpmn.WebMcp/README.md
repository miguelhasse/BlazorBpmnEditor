# Hasseware.AspNetCore.Components.Bpmn.WebMcp (preview)

Offers an open BPMN editor to browser agents through [WebMCP](https://webmachinelearning.github.io/webmcp/).
**Read-only by default**: an agent can look at the diagram, validate it and export it. With
`AllowMutations = true` it can also edit, and deleting and saving ask the user first. The tools, their names and
the component's parameters may still change.

```csharp
// Program.cs
builder.Services.AddBpmnComponents().AddBpmnWebMcp();
```

```razor
<BpmnEditorScope Document="@document">
    <BpmnWebMcp />
    @* … the editor parts … *@
</BpmnEditorScope>
```

`BpmnWebMcp` draws nothing. In a browser without WebMCP it does nothing, quietly: `Status` is `Unsupported`
and `OnUnavailable` is raised. Tools are registered after the first render, never while prerendering, and
withdrawn when the component is disposed.

## Tools

The tools of `Hasseware.AspNetCore.Components.Bpmn.Tools`, under a prefix you can change. The first six are always offered:

| Tool | Returns |
|---|---|
| `bpmn_get_diagram` | Diagrams, elements and connectors, a page at a time |
| `bpmn_describe_element` | One element's properties, references and findings |
| `bpmn_get_selection` | What the user has selected |
| `bpmn_validate` | The editor's validation, with rule ids |
| `bpmn_export` | BPMN 2.0 XML or the JSON interchange format |
| `bpmn_list_creatable` | What the palette offers in a container |

With `AllowMutations`; the last two only for an editor bound to a stored document:

| Tool | Does |
|---|---|
| `bpmn_create_element` | A Flow Node beside an anchor or inside a container, placed automatically; optionally connected from the anchor |
| `bpmn_connect` | A connector, refused when BPMN forbids it |
| `bpmn_set_property` | One standard property |
| `bpmn_delete` | Elements and what cannot outlive them, all listed. **Asks the user first** |
| `bpmn_select` | Selects elements so the user sees what the agent means; changes no content |
| `bpmn_undo`, `bpmn_redo` | Steps through the agent's **own** edits; refused when the next step is the user's |
| `bpmn_get_document_info` | Stored name, stored revision, `isDirty`, and `externalChange` (`none`, `updated`, `deleted`) |
| `bpmn_save` | Saves to the stored document. **Asks the user first**; `PERSIST-CONFLICT` when the stored copy moved |

Text from the diagram (names, documentation) is always returned as `{ "untrustedText": "…" }`, never as the
tool's own words, so a name such as "ignore previous instructions" reaches the agent as data.

Every result is a plain object with `ok`, `code`, `message`, `diagnostics` and `revision`. A tool never
rejects: failures are results with `ok: false`.

## Options

```csharp
builder.Services.AddBpmnWebMcp(options =>
{
    options.ToolPrefix = "diagram_";                 // diagram_get_diagram, …
    options.DisabledTools.Add("bpmn_export");        // keep the whole document from agents
    options.MaxElementsPerCall = 100;
    options.MaxXmlBytes = 500_000;
    options.AllowMutations = true;                   // offer the editing tools; default false
    // options.RequireConfirmationForConsequential = true;   // ask before bpmn_delete (default)
    // options.RequireConfirmationForPersistence = true;     // ask before bpmn_save (default)
});
```

Several editors on one page need an `InstanceName` each (`<BpmnWebMcp InstanceName="left" />` registers
`bpmn_get_diagram_left`); an unnamed second one is suffixed `_2` and a warning is logged.

## Browser support

WebMCP is a draft (the specification was last read 2026-10-15). In Chrome it is an origin trial announced to end
2026-11-17, so a site needs a trial token or a flag; that is the host's concern. The
module uses `document.modelContext` and falls back to `navigator.modelContext`, and it bundles no polyfill; a
host that wants one can load `@mcp-b/webmcp-polyfill` before Blazor starts.

## Same origin only

The tools are never exposed to other origins (`exposedTo` is never set), and annotations such as
`readOnlyHint` are hints for the browser, not enforcement: the bridge enforces read-only, confirmation and the
stale-document rule itself, on every call. Edits join the user's undo history, marked as the agent's.
