namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// Thrown when input cannot be read as a BPMN document at all.
/// </summary>
/// <remarks>
/// Reserved for failures that leave nothing to show: malformed XML, or a root element that is not
/// BPMN <c>definitions</c>. Everything a reader can recover from is reported as a
/// <see cref="BpmnImportDiagnostic"/> instead, so a partly broken document still opens and the
/// user can see what is wrong with it. Catch <see cref="BpmnImportException"/> to handle XML and
/// JSON input alike.
/// </remarks>
public sealed class BpmnXmlException : BpmnImportException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What made the input unreadable.</param>
    public BpmnXmlException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying parse failure.</summary>
    /// <param name="message">What made the input unreadable.</param>
    /// <param name="innerException">The underlying failure.</param>
    public BpmnXmlException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
