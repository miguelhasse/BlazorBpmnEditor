namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>Identifies one stored document: a tenant and a document path within it.</summary>
/// <remarks>
/// <para>
/// The tenant partitions the document space. It must come from the authenticated principal or the
/// host's own configuration, never from text an agent or a client supplied; stores apply it, so tool
/// and component code cannot forget it. <see langword="null"/> is the single, unpartitioned space.
/// </para>
/// <para>
/// A key always names a document, so its path must end in <c>.bpmn</c>, <c>.xml</c> or <c>.json</c>.
/// <see langword="default"/> names nothing and every store refuses it.
/// </para>
/// </remarks>
public readonly record struct BpmnDocumentKey
{
    /// <summary>Creates a key.</summary>
    /// <param name="tenant">The tenant, or <see langword="null"/> for the single, unpartitioned space.</param>
    /// <param name="path">The document's path; its name must have a document extension.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> does not name a document.</exception>
    public BpmnDocumentKey(string? tenant, BpmnDocumentPath path)
    {
        if (!path.IsDocumentName)
        {
            throw new ArgumentException(
                $"'{path}' does not name a document; the name must end in .bpmn, .xml or .json.", nameof(path));
        }

        Tenant = tenant;
        Path = path;
    }

    /// <summary>The longest tenant name the stores accept.</summary>
    public const int MaxTenantLength = 128;

    /// <summary>
    /// Whether a name can be a tenant: one valid path segment of at most <see cref="MaxTenantLength"/> characters, as every store requires.
    /// </summary>
    /// <remarks>
    /// For the code that takes a tenant from somewhere it does not control, such as a claim: an email address or a URL is a perfectly good claim
    /// and not a tenant, and finding that out as an exception from a store is finding out late.
    /// </remarks>
    /// <param name="tenant">The name.</param>
    /// <returns>True when the stores will accept it.</returns>
    public static bool IsValidTenant(string? tenant) =>
        !string.IsNullOrEmpty(tenant)
        && tenant.Length <= MaxTenantLength
        && !tenant.Contains('/', StringComparison.Ordinal)
        && BpmnDocumentPath.TryParse(tenant, out var parsed)
        && string.Equals(parsed.ToString(), tenant, StringComparison.Ordinal);

    /// <summary>The tenant, or <see langword="null"/> for the single, unpartitioned space.</summary>
    public string? Tenant { get; }

    /// <summary>The document's path within the tenant.</summary>
    public BpmnDocumentPath Path { get; }

    /// <summary>Whether this key names a document; false only for <see langword="default"/>.</summary>
    public bool IsValid => Path.IsDocumentName;

    /// <summary>The key as text, for logs: the tenant, if any, then the path.</summary>
    /// <returns>A diagnostic representation.</returns>
    public override string ToString() => Tenant is null ? Path.ToString() : $"{Tenant}:{Path}";
}
