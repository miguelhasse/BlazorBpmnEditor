# Lua script validation (optional)

Install `Hasseware.AspNetCore.Components.Bpmn.Scripting.Lua` alongside the core BPMN package:

```csharp
builder.Services.AddBpmnComponents().AddBpmnLuaScripting();
```

Script Tasks whose `scriptFormat` names Lua then get their script parsed during validation, and a
syntax error is reported as `PROF-116` with its line and column. The script is only parsed, never
run. Rules that are not about syntax (a missing `scriptFormat`, an empty or oversized script, formats
the engine does not run) live in the core package and are configured with `BpmnScriptOptions`.

Lua 5.4 is assumed. If your engine runs an older or different dialect, say so, because `//`, the
bitwise operators and goto are errors in some of them:

```csharp
using Loretta.CodeAnalysis.Lua;

builder.Services.AddBpmnComponents().AddBpmnLuaScripting(LuaSyntaxOptions.Lua51);
```

Parsing is done by [Loretta](https://github.com/LorettaDevs/Loretta), which reports every syntax
error in a script rather than only the first. It checks syntax only: a `break` outside a loop or a
`goto` to a missing label is not reported, and the engine remains the judge of those.
