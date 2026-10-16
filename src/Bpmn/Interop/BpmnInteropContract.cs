namespace Hasseware.AspNetCore.Components.Bpmn.Interop;

/// <summary>
/// Constants describing the JavaScript module this library talks to.
/// </summary>
/// <remarks>
/// <see cref="Version"/> is asserted equal to the <c>INTEROP_CONTRACT_VERSION</c> literal in
/// <c>src/interop/contract-version.ts</c> by a unit test, and <c>BpmnCanvas</c> refuses to run
/// against a module reporting a different value. A mismatched bundle is otherwise very hard to
/// diagnose: it fails later, somewhere unrelated, with a shape that looks like a data bug.
/// </remarks>
public static class BpmnInteropContract
{
    /// <summary>The interop contract version this assembly implements.</summary>
    public const string Version = "2.3";

    /// <summary>Static web asset path of the canvas module, as imported by <c>BpmnCanvas</c>.</summary>
    public const string ModulePath =
        "./_content/Hasseware.AspNetCore.Components.Bpmn/Hasseware.AspNetCore.Components.Bpmn.lib.module.js";

    /// <summary>
    /// Static web asset path of the library stylesheet. Consuming applications must link this
    /// themselves; the library never injects a stylesheet into the host document.
    /// </summary>
    public const string StylesheetPath =
        "_content/Hasseware.AspNetCore.Components.Bpmn/Hasseware.AspNetCore.Components.Bpmn.bundle.scp.css";
}
