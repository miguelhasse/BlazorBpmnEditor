namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// Thrown when input cannot be read as a BPMN document at all, whatever its interchange format.
/// </summary>
/// <remarks>
/// The common base of <see cref="BpmnXmlException"/> and <see cref="BpmnJsonException"/>, so a
/// caller that accepts either format — a file picker, a drop target — can catch one type. As with
/// the format-specific types, it is reserved for input that leaves nothing to show; everything a
/// reader can recover from is reported as a <see cref="BpmnImportDiagnostic"/> instead.
/// </remarks>
public abstract class BpmnImportException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What made the input unreadable.</param>
    protected BpmnImportException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying parse failure.</summary>
    /// <param name="message">What made the input unreadable.</param>
    /// <param name="innerException">The underlying failure.</param>
    protected BpmnImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
