# C# script validation (optional)

Install `Hasseware.AspNetCore.Components.Bpmn.Scripting.CSharp` alongside the core BPMN package:

```csharp
builder.Services.AddBpmnComponents().AddBpmnCSharpScripting();
```

Script Tasks whose `scriptFormat` names C# then get their script parsed during validation, and a
syntax error is reported as `PROF-116` with its line and column. The script is only parsed, never
run. Rules that are not about syntax (a missing `scriptFormat`, an empty or oversized script, formats
the engine does not run) live in the core package and are configured with `BpmnScriptOptions`.
