namespace Hasseware.AspNetCore.Components.Bpmn.Interop;

/// <summary>
/// Thrown when the loaded JavaScript bundle implements a different interop contract version
/// than this assembly.
/// </summary>
/// <remarks>
/// Almost always a stale static web asset: a cached bundle from an older package version, or a
/// host that copied <c>wwwroot</c> by hand. Failing loudly at initialisation is far cheaper to
/// diagnose than the missing-property and silent-no-op symptoms a version skew otherwise causes.
/// </remarks>
public sealed class BpmnInteropVersionException : InvalidOperationException
{
    /// <summary>Creates the exception for a specific version mismatch.</summary>
    /// <param name="expected">The contract version this assembly implements.</param>
    /// <param name="actual">The contract version reported by the loaded module.</param>
    public BpmnInteropVersionException(string expected, string actual)
        : base(
            $"The BPMN canvas script implements interop contract '{actual}' but this assembly requires '{expected}'. "
            + "The static web assets are out of date — clear the browser cache and rebuild the consuming application.")
    {
        Expected = expected;
        Actual = actual;
    }

    /// <summary>The contract version this assembly implements.</summary>
    public string Expected { get; }

    /// <summary>The contract version reported by the loaded module.</summary>
    public string Actual { get; }
}
