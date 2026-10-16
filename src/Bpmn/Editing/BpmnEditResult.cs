using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Why an edit was refused, or what it changed on the way through.</summary>
public enum BpmnEditSeverity
{
    /// <summary>The edit was applied; the finding records something the user should know.</summary>
    Info,

    /// <summary>The edit was refused and the document is unchanged.</summary>
    Rejected,
}

/// <summary>One finding from applying an edit.</summary>
/// <param name="Severity">Whether the edit survived.</param>
/// <param name="Code">
/// A stable rule code where one applies — <c>DI-011</c> for a collision, <c>CT-*</c> for
/// containment — so the message can be replaced without breaking anything that keys off it.
/// </param>
/// <param name="Message">What happened, in the terms the user sees.</param>
/// <param name="ElementId">The element the finding is about, when it is about one.</param>
public sealed record BpmnEditDiagnostic(
    BpmnEditSeverity Severity,
    string Code,
    string Message,
    string? ElementId = null);

/// <summary>
/// The outcome of one command.
/// </summary>
/// <remarks>
/// On rejection <see cref="Document"/> is the document that went in, unchanged. Callers therefore
/// do not need to decide whether to keep their old reference — the result is always the document
/// to use next, whichever way it went.
/// </remarks>
/// <param name="Document">The document after the command, or the original if it was refused.</param>
/// <param name="IsApplied">Whether anything changed.</param>
/// <param name="Diagnostics">Every finding, in the order they were produced.</param>
public sealed record BpmnEditResult(
    BpmnDocument Document,
    bool IsApplied,
    IReadOnlyList<BpmnEditDiagnostic> Diagnostics)
{
    /// <summary>
    /// The elements the command brought into existence, when it created any.
    /// </summary>
    /// <remarks>
    /// Empty for a command that created nothing, which is most of them. It exists because a caller
    /// that has just pasted or created something almost always wants to select it, and diffing two
    /// documents to work out what appeared is both slower and wrong the moment two commands are
    /// applied between renders.
    /// </remarks>
    public IReadOnlyList<string> CreatedIds { get; init; } = [];

    internal IReadOnlySet<string>? DeletedElementIds { get; init; }

    /// <summary>The reasons the command was refused, if it was.</summary>
    public IEnumerable<BpmnEditDiagnostic> Rejections =>
        Diagnostics.Where(diagnostic => diagnostic.Severity == BpmnEditSeverity.Rejected);

    /// <summary>A refusal that leaves the document exactly as it was.</summary>
    internal static BpmnEditResult Reject(BpmnDocument document, params BpmnEditDiagnostic[] diagnostics) =>
        new(document, false, diagnostics);

    /// <summary>A successful application.</summary>
    internal static BpmnEditResult Applied(
        BpmnDocument document,
        IReadOnlyList<BpmnEditDiagnostic>? diagnostics = null) =>
        new(document, true, diagnostics ?? []);
}
