namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// Thrown when a document cannot be written in the requested format at all.
/// </summary>
/// <remarks>
/// For example an extension element whose name is not a valid XML name, or a tree nested more deeply than the JSON writer allows. A document that reads is not
/// always one that writes: these come from content a document brought with it, not from a fault in the caller.
/// </remarks>
public sealed class BpmnExportException : Exception
{
    /// <summary>Creates the exception with a message and the underlying failure.</summary>
    /// <param name="message">What made the document unwritable.</param>
    /// <param name="innerException">The underlying failure.</param>
    public BpmnExportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
