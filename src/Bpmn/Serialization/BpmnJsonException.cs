namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// Thrown when input cannot be read as a BPMN JSON document at all.
/// </summary>
/// <remarks>
/// The JSON counterpart of <see cref="BpmnXmlException"/>: malformed JSON, a document that exceeds
/// the configured size or depth limits, a missing or foreign <c>format</c> header, or a
/// <c>version</c> whose major number this library does not understand. Catch
/// <see cref="BpmnImportException"/> to handle XML and JSON input alike.
/// </remarks>
public sealed class BpmnJsonException : BpmnImportException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What made the input unreadable.</param>
    public BpmnJsonException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying parse failure.</summary>
    /// <param name="message">What made the input unreadable.</param>
    /// <param name="innerException">The underlying failure.</param>
    public BpmnJsonException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
