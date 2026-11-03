namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Optional behavior attached to an existing property text control.</summary>
public sealed class BpmnPropertyInputEnhancement
{
    /// <summary>Whether the native bridge owns draft and commit events.</summary>
    public bool IsEnhanced { get; set; }

    /// <summary>Receives fallback drafts before native enhancement is available.</summary>
    public Func<string?, Task>? DraftChanged { get; init; }

    /// <summary>Receives fallback keyboard actions when the native bridge is unavailable.</summary>
    public Func<Microsoft.AspNetCore.Components.Web.KeyboardEventArgs, Task>? KeyDown { get; init; }

    /// <summary>Optional attached selector rendered beside the existing input.</summary>
    public Microsoft.AspNetCore.Components.RenderFragment? Suffix { get; set; }
}
