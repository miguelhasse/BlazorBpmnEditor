using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

// ParameterView expires after assignment. Keep values, not the view, and reuse the small map.
internal sealed class BpmnStateRenderGate
{
    private readonly Dictionary<string, object?> _parameters = new(StringComparer.Ordinal);
    private CultureInfo? _culture;
    private CultureInfo? _uiCulture;
    private bool _initialized;

    public bool IsStateOnly(ParameterView parameters, string stateName, BpmnEditorState? previous)
    {
        var same = _initialized && ReferenceEquals(_culture, CultureInfo.CurrentCulture)
            && ReferenceEquals(_uiCulture, CultureInfo.CurrentUICulture);
        var changedState = false;
        var count = 0;
        var previousCount = _parameters.Count;
        foreach (var parameter in parameters)
        {
            if (parameter.Name == stateName)
            {
                changedState = previous is not null && parameter.Value is BpmnEditorState current
                    && !ReferenceEquals(previous, current);
                continue;
            }
            count++;
            same &= _parameters.TryGetValue(parameter.Name, out var old) && ReferenceEquals(old, parameter.Value);
            _parameters[parameter.Name] = parameter.Value;
        }
        same &= count == previousCount;
        if (count != previousCount)
        {
            _parameters.Clear();
            foreach (var parameter in parameters)
                if (parameter.Name != stateName) _parameters[parameter.Name] = parameter.Value;
        }
        _initialized = true;
        _culture = CultureInfo.CurrentCulture;
        _uiCulture = CultureInfo.CurrentUICulture;
        return same && changedState;
    }
}
