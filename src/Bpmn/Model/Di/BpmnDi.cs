using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Di;

/// <summary>An axis-aligned rectangle in diagram coordinates.</summary>
public readonly record struct Bounds(double X, double Y, double Width, double Height)
{
    /// <summary>True when the rectangle has positive area, as BPMNDI requires (D015).</summary>
    public bool IsPositive => Width > 0d && Height > 0d;
}

/// <summary>A point on a connector route.</summary>
public readonly record struct DiPoint(double X, double Y);

/// <summary>
/// Font and decoration settings a label may reference (specification section 6).
/// </summary>
/// <remarks>
/// Shaped after <c>dc:Font</c> rather than after CSS, because that is what the file contains and
/// what a round trip has to put back. Mapping the four flags onto <c>font-weight</c> and friends is
/// the renderer's job, not the model's.
/// </remarks>
/// <param name="Id">Style id, referenced by a label's <c>labelStyle</c> attribute.</param>
/// <param name="FontFamily">The <c>name</c> attribute of the font.</param>
/// <param name="FontSize">Point size.</param>
/// <param name="IsBold">Whether the font is bold.</param>
/// <param name="IsItalic">Whether the font is italic.</param>
/// <param name="IsUnderline">Whether the text is underlined.</param>
/// <param name="IsStrikeThrough">Whether the text is struck through.</param>
public sealed record BpmnLabelStyle(
    string Id,
    string? FontFamily = null,
    double? FontSize = null,
    bool? IsBold = null,
    bool? IsItalic = null,
    bool? IsUnderline = null,
    bool? IsStrikeThrough = null);

/// <summary>
/// A label's persisted geometry and style.
/// </summary>
/// <remarks>
/// Carries no text, by design. In BPMNDI a <c>BPMNLabel</c> says where a label is drawn and how
/// it is styled; the string itself always comes from the semantic element's name (or a Text
/// Annotation's content). Modelling text here would duplicate the semantic model and, worse,
/// shadow it with an empty value whenever a document supplies label geometry.
/// </remarks>
/// <param name="Id">DI id, when the document declares one.</param>
/// <param name="Bounds">
/// Explicit label bounds. When present they are authoritative and fixed, so text that exceeds
/// them clips or ellipsises visually while the full string stays intact.
/// </param>
/// <param name="StyleId">A referenced <see cref="BpmnLabelStyle"/>.</param>
public sealed record BpmnLabel(string? Id = null, Bounds? Bounds = null, string? StyleId = null);

/// <summary>
/// The DI entry for a drawn element.
/// </summary>
/// <param name="Id">DI id.</param>
/// <param name="BpmnElement">The semantic element this shape draws.</param>
/// <param name="Bounds">Position and size, used as stored and never recomputed (sections 10.6 and 11).</param>
/// <param name="IsHorizontal">
/// Pool and Lane orientation. Meaningful only on those shapes and <see langword="null"/>
/// everywhere else (section 6 note, section 10.4); a Lane must agree with its Pool (DI-006).
/// </param>
/// <param name="IsExpanded">
/// Sub-Process expansion state. A collapsed container draws as an opaque leaf and its children
/// are not drawn on this plane (section 2.7).
/// </param>
/// <param name="IsMarkerVisible">Whether an Exclusive Gateway draws its X marker.</param>
/// <param name="Label">The shape's label, when the document supplies one.</param>
public sealed record BpmnShape(
    string Id,
    string BpmnElement,
    Bounds Bounds,
    bool? IsHorizontal = null,
    bool? IsExpanded = null,
    bool? IsMarkerVisible = null,
    BpmnLabel? Label = null)
{
    /// <summary>DI-level extension content, preserved verbatim (section 10.5).</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>
/// The DI entry for a connector.
/// </summary>
/// <param name="Id">DI id.</param>
/// <param name="BpmnElement">The connector this edge draws.</param>
/// <param name="Waypoints">
/// The persisted route, including both endpoints. BPMNDI requires at least two (D015), and they
/// are preserved exactly so a round trip cannot reroute a diagram.
/// </param>
/// <param name="Label">The connector's label, when the document supplies one.</param>
public sealed record BpmnEdge(
    string Id,
    string BpmnElement,
    IReadOnlyList<DiPoint> Waypoints,
    BpmnLabel? Label = null)
{
    /// <summary>DI-level extension content, preserved verbatim (section 10.5).</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}


/// <summary>
/// A plane: the drawing surface for one Process, Collaboration or expanded Sub-Process.
/// </summary>
public sealed record BpmnPlane(
    string Id,
    string? BpmnElement,
    IReadOnlyList<BpmnShape> Shapes,
    IReadOnlyList<BpmnEdge> Edges)
{
    /// <summary>DI-level extension content, preserved verbatim (section 10.5).</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}


/// <summary>
/// A diagram and its plane. A document may declare several, which the editor treats as
/// navigable rather than as one fixed canvas.
/// </summary>
public sealed record BpmnDiagram(string Id, BpmnPlane Plane, string? Name = null)
{
    /// <summary>
    /// Label styles this diagram declares, which its labels reference by id.
    /// </summary>
    /// <remarks>
    /// Per diagram rather than per document because that is where <c>BPMNDI.xsd</c> puts them, and
    /// a round trip that relocated them would change the file's shape for no reason.
    /// </remarks>
    public IReadOnlyList<BpmnLabelStyle> LabelStyles { get; init; } = [];

    /// <summary>DI-level extension content, preserved verbatim (section 10.5).</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}
