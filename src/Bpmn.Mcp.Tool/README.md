# bpmn-mcp (preview)

A command-line tool for BPMN diagrams: it serves a folder of them to AI agents over MCP, and it validates `.bpmn` files
in a build pipeline.

```
dotnet tool install --global Hasseware.AspNetCore.Components.Bpmn.Mcp.Tool
```

## Serve a folder to an agent

```
bpmn-mcp serve --root ./diagrams
```

An MCP client launches it as a child process and talks over stdin and stdout; every log line goes to stderr, so
the protocol is never interrupted. Unless you pass `--allow-writes` it only reads: the agent can list, open, validate, describe, export and compare the
documents under the folder, and name them only by logical path (`claims/intake.bpmn`), never by file path.
`--tenant <name>` confines it to that tenant's folder. `--allow-writes` additionally lets it create documents and edit
them; each edit is saved at the revision it was made against, so a concurrent change is refused rather than overwritten.
Without the flag nothing can change. See
[the MCP package](../Bpmn.Mcp/README.md) for the tools.

## Validate in CI

```
bpmn-mcp validate diagrams            # every .bpmn under the folder
bpmn-mcp validate "src/**/*.bpmn" --warnings-as-errors --json
```

One line per finding (`file: severity CODE message [element]`), or JSON with `--json`. The findings are exactly what
an agent's `bpmn_validate` reports. Exit code **0** when clean, **1** when there are errors (or warnings with
`--warnings-as-errors`, or a file that is not BPMN), **2** when an argument or file cannot be used, so a broken
invocation never passes as a clean diagram.
