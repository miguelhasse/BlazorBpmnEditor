using System.Text.RegularExpressions;

namespace SqlServerChangeIngress;

/// <summary>How the ingress finds out about changes.</summary>
public enum SqlServerIngressMode
{
    /// <summary>A trigger sends a Service Broker message; the ingress waits on the queue. Push, and durable while the ingress is down.</summary>
    ServiceBroker,

    /// <summary>The table is change-tracked; the ingress asks for changes on an interval. Pull, with no trigger.</summary>
    ChangeTracking,
}

/// <summary>The <c>Ingress</c> configuration section. The defaults match the scripts and the EF Core store's default table.</summary>
public sealed partial class SqlServerIngressOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Ingress";

    /// <summary>How changes are found.</summary>
    public SqlServerIngressMode Mode { get; set; } = SqlServerIngressMode.ServiceBroker;

    /// <summary>The documents table, as <c>schema.table</c>.</summary>
    public string Table { get; set; } = "dbo.BpmnDocuments";

    /// <summary>The Service Broker queue the trigger's messages arrive on, as <c>schema.queue</c>.</summary>
    public string Queue { get; set; } = "dbo.BpmnDocumentChangeQueue";

    /// <summary>The message type the trigger sends.</summary>
    public string MessageType { get; set; } = "//Hasseware/Bpmn/DocumentChanged";

    /// <summary>How long one <c>WAITFOR (RECEIVE …)</c> waits before it is issued again.</summary>
    public TimeSpan ReceiveTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often Change Tracking is asked for changes.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long to wait before reconnecting after the database could not be reached.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Quotes a <c>schema.name</c> from configuration for use in a statement, refusing anything that is not two plain identifiers.</summary>
    /// <param name="name">The name.</param>
    /// <returns><c>[schema].[name]</c>.</returns>
    /// <exception cref="InvalidOperationException">The name is not two plain identifiers.</exception>
    public static string QuoteTwoPartName(string name)
    {
        var parts = name.Split('.');
        if (parts.Length != 2 || !parts.All(part => Identifier().IsMatch(part)))
        {
            throw new InvalidOperationException($"'{name}' is not a two-part name such as dbo.BpmnDocuments.");
        }

        return $"[{parts[0]}].[{parts[1]}]";
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
