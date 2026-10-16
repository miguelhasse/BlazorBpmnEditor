using System.Globalization;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp;

/// <summary>
/// Hands each <see cref="BpmnWebMcp"/> on a page a name suffix of its own, because a browser refuses two
/// tools with one name and the tools of two editors would otherwise collide.
/// </summary>
/// <remarks>Scoped: one per circuit, or per browser tab in WebAssembly.</remarks>
public sealed class BpmnWebMcpRegistry
{
    private static readonly TimeSpan ReleaseWait = TimeSpan.FromSeconds(2);

    // A suffix in use, and, once its editor is going away, what completes when it has withdrawn its tools.
    private readonly Dictionary<string, TaskCompletionSource?> _claimed = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>Claims a suffix.</summary>
    /// <param name="instanceName">The name the host gave the editor, or null.</param>
    /// <param name="renamed">Whether the suffix differs from what was asked for, which a host would want to know.</param>
    /// <returns>The suffix to append to every tool name; empty for the first unnamed editor.</returns>
    /// <exception cref="ArgumentException">The name has characters a tool name may not.</exception>
    public string Claim(string? instanceName, out bool renamed)
    {
        var wanted = Wanted(instanceName);
        lock (_lock)
        {
            return ClaimFree(wanted, out renamed);
        }
    }

    /// <summary>
    /// Claims a suffix like <see cref="Claim"/>, except that one held by an editor already going away is waited
    /// for, briefly: while navigating, the new editor mounts before the old one has withdrawn its tools, and
    /// it should still get the plain names.
    /// </summary>
    /// <param name="instanceName">The name the host gave the editor, or null.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>The suffix, and whether it differs from what was asked for.</returns>
    /// <exception cref="ArgumentException">The name has characters a tool name may not.</exception>
    public async Task<(string Suffix, bool Renamed)> ClaimAsync(string? instanceName, CancellationToken cancellationToken = default)
    {
        var wanted = Wanted(instanceName);
        while (true)
        {
            Task? leaving = null;
            lock (_lock)
            {
                if (!_claimed.TryGetValue(wanted, out var release) || release is null)
                {
                    var suffix = ClaimFree(wanted, out var renamed);
                    return (suffix, renamed);
                }

                leaving = release.Task;
            }

            await Task.WhenAny(leaving, Task.Delay(ReleaseWait, cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                // Still there after the wait: it is not leaving; number past it.
                if (_claimed.TryGetValue(wanted, out var stuck) && stuck is not null && !stuck.Task.IsCompleted)
                {
                    var suffix = ClaimFree(wanted, out var renamed);
                    return (suffix, renamed);
                }
            }
        }
    }

    /// <summary>Says that the editor holding a suffix is going away, so a new one may wait for it.</summary>
    /// <param name="suffix">What <see cref="Claim"/> returned.</param>
    public void BeginRelease(string suffix)
    {
        lock (_lock)
        {
            if (_claimed.TryGetValue(suffix, out var release) && release is null)
            {
                _claimed[suffix] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    /// <summary>Gives a suffix back, so another editor may use it.</summary>
    /// <param name="suffix">What <see cref="Claim"/> returned.</param>
    public void Release(string suffix)
    {
        TaskCompletionSource? release;
        lock (_lock)
        {
            _claimed.Remove(suffix, out release);
        }

        release?.TrySetResult();
    }

    private static string Wanted(string? instanceName)
    {
        if (instanceName is not null && !IsValid(instanceName))
        {
            throw new ArgumentException("An instance name may use letters, digits, '_' and '-' only.", nameof(instanceName));
        }

        return string.IsNullOrEmpty(instanceName) ? string.Empty : "_" + instanceName;
    }

    private string ClaimFree(string wanted, out bool renamed)
    {
        var suffix = wanted;
        for (var number = 2; !_claimed.TryAdd(suffix, null); number++)
        {
            suffix = wanted + "_" + number.ToString(CultureInfo.InvariantCulture);
        }

        renamed = suffix != wanted;
        return suffix;
    }

    private static bool IsValid(string name) => name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
