---
title: Blazor Performance
impact: MEDIUM-HIGH
impactDescription: Poor rendering patterns cause slow, unresponsive Blazor applications
tags: blazor, performance, rendering, optimization, parameters
---

## Blazor Performance

Reference: [Blazor Performance](https://docs.microsoft.com/en-us/aspnet/core/blazor/performance)

### Keep Parameter-Dependent Work Efficient

Use `OnInitializedAsync` or `OnInitialized` for one-time initialization. Use
`OnParametersSetAsync` or `OnParametersSet` for work that depends on current parameters; these
methods run after parameter assignment and may run on every parent render. Cache the inputs and
skip expensive work when they have not changed.

```csharp
// Avoid repeating expensive work when the parameter value is unchanged
protected override async Task OnParametersSetAsync()
{
    if (_lastValue == Value)
    {
        return;
    }

    _lastValue = Value;
    _data = await ComputeExpensiveResultAsync(Value);
}

// Correct: called only once during initialization
protected override async Task OnInitializedAsync()
{
    _data = await DataService.LoadAsync();
}
```

Reference: [ASP.NET Core Blazor component lifecycle](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/lifecycle)

### Override SetParametersAsync Only When Needed

`SetParametersAsync` is a lower-level parameter-assignment hook, not a general replacement for
`OnParametersSet` or `OnParametersSetAsync`. Override it only when parameter processing itself
needs customization or profiling demonstrates a benefit. Call `base.SetParametersAsync(parameters)`
so Blazor assigns parameters and runs the normal lifecycle.

If overriding it, use `ParameterView.TryGetValue<T>` only when the incoming parameter values must be
inspected before normal assignment. Compare them with cached inputs before doing expensive work,
and ensure base parameter assignment and lifecycle processing still occur.

```csharp
public override async Task SetParametersAsync(ParameterView parameters)
{
    if (parameters.TryGetValue<int>(nameof(Value), out var newValue) && newValue != _lastValue)
    {
        _lastValue = newValue;
        _data = ComputeExpensiveResult(newValue);
    }

    await base.SetParametersAsync(parameters);
}
```

### Avoid Unnecessary Rendering
1. Ensure child component parameters are of **primitive immutable types**.
2. Override [ShouldRender](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.components.componentbase.shouldrender) when appropriate.

### Create Lightweight Components
- Avoid thousands of **component instances** — inline child components into their parents when needed.

### Minimize Parameters
- Don't receive too many parameters. Bundle them in a custom class:

```csharp
[Parameter]
public GridOptions? Options { get; set; }
```

### Fix Cascading Parameters

```xml
<CascadingValue Value="this" IsFixed="true">
    <SomeOtherComponents>
</CascadingValue>
```

### Events
- Don't trigger events too rapidly (e.g., `onmousemove`, `onscroll`).
- **Avoid rerendering** after handling events without state changes (by default `StateHasChanged` is called automatically).

### Other Guidelines
- Avoid attribute splatting with **CaptureUnmatchedValues**.
- Avoid recreating **delegates** for repeated elements.
- Optimize **JavaScript** interop speed — minimize calls between .NET and JS.
