using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Tools;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp;

/// <summary>What a tool tells the browser about itself; the module turns it into WebMCP's hints.</summary>
/// <param name="ReadOnly">The tool changes nothing.</param>
/// <param name="Consequential">The tool can destroy or publish work, so a user should be asked first.</param>
/// <param name="UntrustedContent">The result carries text from the diagram, which anyone able to edit it could have written.</param>
public sealed record BpmnWebMcpAnnotations(bool ReadOnly, bool Consequential, bool UntrustedContent);

/// <summary>A tool as it is registered with the browser.</summary>
/// <param name="Name">The registered name, with the prefix and instance suffix applied.</param>
/// <param name="Description">What the agent is told it does.</param>
/// <param name="InputSchema">The arguments, as a JSON Schema.</param>
/// <param name="Annotations">What the tool changes and returns.</param>
public sealed record BpmnWebMcpToolDescriptor(string Name, string Description, JsonElement InputSchema, BpmnWebMcpAnnotations Annotations);

/// <summary>The tools one editor offers, under the names it offers them by.</summary>
/// <remarks>
/// Built from <see cref="BpmnToolCatalog"/>, so the browser surface and a server surface list the same tools.
/// The read-only ones are always offered; the ones that change the diagram only when the host allows it, and
/// the persistence ones only when the editor is bound to a stored document.
/// </remarks>
public sealed class BpmnWebMcpToolSet
{
    private const string CatalogPrefix = "bpmn_";
    internal const string PersistenceToolInfo = "bpmn_get_document_info";
    internal const string PersistenceToolSave = "bpmn_save";

    private readonly Dictionary<string, string> _catalogNames;

    private BpmnWebMcpToolSet(IReadOnlyList<BpmnWebMcpToolDescriptor> descriptors, Dictionary<string, string> catalogNames)
    {
        Descriptors = descriptors;
        _catalogNames = catalogNames;
    }

    /// <summary>The tools to register.</summary>
    public IReadOnlyList<BpmnWebMcpToolDescriptor> Descriptors { get; }

    /// <summary>Builds the tool set.</summary>
    /// <param name="prefix">The prefix replacing <c>bpmn_</c> on every name.</param>
    /// <param name="suffix">The instance suffix appended to every name; empty for none.</param>
    /// <param name="disabled">Catalog names of tools to leave out.</param>
    /// <param name="allowMutations">Whether tools that change the diagram are offered.</param>
    /// <param name="hasDocumentStore">Whether the editor is bound to a stored document, which the persistence tools need.</param>
    /// <returns>The tool set.</returns>
    /// <exception cref="ArgumentException">The prefix is empty or has characters a tool name may not.</exception>
    public static BpmnWebMcpToolSet Create(
        string prefix,
        string suffix,
        IEnumerable<string>? disabled = null,
        bool allowMutations = false,
        bool hasDocumentStore = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentNullException.ThrowIfNull(suffix);
        if (!prefix.All(IsNameCharacter)) throw new ArgumentException("A tool prefix may use letters, digits, '_', '-' and '.' only.", nameof(prefix));

        var off = disabled?.ToHashSet(StringComparer.Ordinal) ?? [];
        var descriptors = new List<BpmnWebMcpToolDescriptor>();
        var catalogNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var tool in BpmnToolCatalog.All.Where(tool => !off.Contains(tool.Name) && IsOffered(tool, allowMutations, hasDocumentStore)))
        {
            var name = prefix + tool.Name[CatalogPrefix.Length..] + suffix;
            using var schema = JsonDocument.Parse(tool.InputSchema);

            descriptors.Add(new BpmnWebMcpToolDescriptor(
                name,
                tool.Description,
                schema.RootElement.Clone(),
                new BpmnWebMcpAnnotations(tool.Annotations.ReadOnly, tool.Annotations.Destructive, tool.Annotations.UntrustedContent)));
            catalogNames[name] = tool.Name;
        }

        return new BpmnWebMcpToolSet(descriptors, catalogNames);
    }

    /// <summary>Whether a catalog tool is one a bridge may be asked to run, which is also what the toolset registers.</summary>
    /// <param name="tool">The catalog tool.</param>
    /// <param name="allowMutations">Whether tools that change the diagram are offered.</param>
    /// <param name="hasDocumentStore">Whether the editor is bound to a stored document.</param>
    /// <returns>Whether to offer it.</returns>
    internal static bool IsOffered(BpmnToolDescriptor tool, bool allowMutations, bool hasDocumentStore)
    {
        if (tool.Name is PersistenceToolInfo or PersistenceToolSave && !hasDocumentStore) return false;

        return tool.Annotations.ReadOnly || allowMutations;
    }

    /// <summary>Finds the catalog tool behind a registered name.</summary>
    /// <param name="registeredName">The name the agent called.</param>
    /// <returns>The catalog name, or null when this editor does not offer a tool of that name.</returns>
    public string? CatalogNameOf(string registeredName) => _catalogNames.GetValueOrDefault(registeredName);

    private static bool IsNameCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.';
}
