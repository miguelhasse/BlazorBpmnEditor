using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Validation;

/// <summary>
/// Whether what a container draws agrees with whether it says it is expanded.
/// </summary>
/// <remarks>
/// <para>
/// Section 5.3 reserves two ids for this and neither was implemented. CT-007 is the one that
/// matters: section 2.8.1 says a collapsed Sub-Process is an opaque leaf that "must not have
/// visible embedded process-content cells", and the renderer draws one cell per shape on the plane
/// with no opinion about who owns it — so a document that leaves a collapsed container's children
/// on the same plane draws them floating over the closed box, parented to the Pool.
/// </para>
/// <para>
/// Neither is reachable through the editor, which is the point. Collapsing moves content to a plane
/// of its own and expanding brings it back, so these fire only for a document assembled some other
/// way — a hand edit, or another tool — and nothing else in the pipeline would notice.
/// </para>
/// </remarks>
public sealed class SubProcessExpansionValidationTests
{
    // --- CT-007: collapsed, but its content is drawn anyway ----------------------------

    [Fact]
    public void ACollapsedSubProcessWhoseContentIsOnThePlaneIsReported()
    {
        var problem = Assert.Single(
            Validate(Nested(isExpanded: false, drawChild: true)),
            diagnostic => diagnostic.RuleId == "CT-007");

        Assert.Equal(BpmnSeverity.Error, problem.Severity);
        Assert.Equal("Sub_1", problem.ElementId);
    }

    [Fact]
    public void AndNamesTheThingItShouldNotBeDrawing()
    {
        var problem = Assert.Single(
            Validate(Nested(isExpanded: false, drawChild: true)),
            diagnostic => diagnostic.RuleId == "CT-007");

        Assert.Contains("Task_inner", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ItReachesGrandchildrenToo()
    {
        // A Task inside a Sub-Process inside the collapsed one is just as undrawable as a direct
        // child, so the walk has to be transitive rather than one level.
        var reported = Validate(DeeplyNested())
            .Where(diagnostic => diagnostic.RuleId == "CT-007")
            .Select(diagnostic => diagnostic.RelatedId)
            .ToList();

        Assert.Contains("Task_deep", reported);
    }

    [Fact]
    public void ACallActivityIsOpaqueWhateverItsShapeSays()
    {
        // Section 1.2.1: a Call Activity never shows a nested graph — there is nothing local to
        // show — so isExpanded="true" on one does not license drawing content inside it.
        var problem = Assert.Single(
            Validate(Nested(isExpanded: true, drawChild: true, type: BpmnFlowNodeType.CallActivity)),
            diagnostic => diagnostic.RuleId == "CT-007");

        Assert.Equal("Sub_1", problem.ElementId);
    }

    [Fact]
    public void AnExpandedSubProcessDrawingItsContentIsExactlyRight()
    {
        // The mutation guard: this is the shape of document the whole feature produces, and a rule
        // that fired on it would make every expanded Sub-Process an error.
        Assert.DoesNotContain(
            Validate(Nested(isExpanded: true, drawChild: true)),
            diagnostic => diagnostic.RuleId is "CT-007" or "CT-009");
    }

    [Fact]
    public void AndACollapsedOneKeepingItsContentElsewhereIsToo()
    {
        Assert.DoesNotContain(
            Validate(Nested(isExpanded: false, drawChild: false)),
            diagnostic => diagnostic.RuleId is "CT-007" or "CT-009");
    }

    // --- CT-009: expanded, and its content is drawn twice ------------------------------

    [Fact]
    public void AnExpandedSubProcessThatAlsoKeepsItsOwnPlaneIsReported()
    {
        /*
         * A plane of its own is where a collapsed container keeps its children. One that survives
         * while the container is expanded leaves no answer to "where does this element live", and
         * drilling in would show a second copy of what is already on screen.
         */
        var problem = Assert.Single(
            Validate(ExpandedWithAStalePlane()),
            diagnostic => diagnostic.RuleId == "CT-009");

        Assert.Equal(BpmnSeverity.Error, problem.Severity);
        Assert.Equal("Sub_1", problem.ElementId);
    }

    [Fact]
    public void AnEmptyLeftoverPlaneIsNotWorthReporting()
    {
        // Nothing is drawn twice, so nothing is ambiguous. A plane with no shapes is untidy, not
        // wrong, and D022 already has an opinion about planes that draw nothing.
        Assert.DoesNotContain(
            Validate(ExpandedWithAnEmptyPlane()),
            diagnostic => diagnostic.RuleId == "CT-009");
    }

    // --- and the shipped fixture -------------------------------------------------------

    [Fact]
    public void TheNestedSubProcessSampleRaisesNeither()
    {
        // Six Sub-Processes, five collapsed with planes of their own and one expanded in place —
        // the arrangement both rules exist to protect, so it had better be clean.
        var document = BpmnXmlReader.Read(
            File.ReadAllText(Path.Combine(Root(), "playground", "Bpmn.Demo", "wwwroot", "samples", "nested-subprocesses.bpmn"))).Document;

        Assert.DoesNotContain(
            BpmnValidator.Validate(document).Diagnostics,
            diagnostic => diagnostic.RuleId is "CT-007" or "CT-009");
    }

    // --- fixtures ----------------------------------------------------------------------

    private static IReadOnlyList<BpmnDiagnostic> Validate(BpmnDocument document) =>
        BpmnValidator.Validate(document).Diagnostics;

    /// <summary>A Sub-Process holding one Task, with the plane drawing the Task or not.</summary>
    private static BpmnDocument Nested(
        bool isExpanded,
        bool drawChild,
        BpmnFlowNodeType type = BpmnFlowNodeType.SubProcess)
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Sub_1", type, new Bounds(200, 100, 350, 200), "Holder", isExpanded: isExpanded)
            .Build();

        var inner = new BpmnFlowNode("Task_inner", BpmnFlowNodeType.UserTask, "Sub_1", "Inside");
        var plane = document.Diagrams[0].Plane;

        return document with
        {
            FlowNodes = [.. document.FlowNodes, inner],
            Diagrams = drawChild
                ?
                [
                    document.Diagrams[0] with
                    {
                        Plane = plane with
                        {
                            Shapes = [.. plane.Shapes, new BpmnShape("Task_inner_di", "Task_inner", new Bounds(240, 140, 100, 80))],
                        },
                    },
                ]
                : document.Diagrams,
        };
    }

    /// <summary>A collapsed Sub-Process whose plane draws a Task two levels down.</summary>
    private static BpmnDocument DeeplyNested()
    {
        var document = Nested(isExpanded: false, drawChild: false);
        var plane = document.Diagrams[0].Plane;

        return document with
        {
            FlowNodes =
            [
                .. document.FlowNodes,
                new BpmnFlowNode("Sub_2", BpmnFlowNodeType.SubProcess, "Sub_1", "Middle", FlowElementsOwned: true),
                new BpmnFlowNode("Task_deep", BpmnFlowNodeType.UserTask, "Sub_2", "Deep"),
            ],
            Diagrams =
            [
                document.Diagrams[0] with
                {
                    Plane = plane with
                    {
                        Shapes = [.. plane.Shapes, new BpmnShape("Task_deep_di", "Task_deep", new Bounds(240, 140, 100, 80))],
                    },
                },
            ],
        };
    }

    /// <summary>An expanded Sub-Process that still has the plane it used when it was collapsed.</summary>
    private static BpmnDocument ExpandedWithAStalePlane() => WithOwnPlane(
        Nested(isExpanded: true, drawChild: true),
        [new BpmnShape("Task_inner_di_2", "Task_inner", new Bounds(0, 0, 100, 80))]);

    private static BpmnDocument ExpandedWithAnEmptyPlane() => WithOwnPlane(
        Nested(isExpanded: true, drawChild: true),
        []);

    private static BpmnDocument WithOwnPlane(BpmnDocument document, IReadOnlyList<BpmnShape> shapes) => document with
    {
        Diagrams =
        [
            .. document.Diagrams,
            new BpmnDiagram("Diagram_sub", new BpmnPlane("Plane_sub", "Sub_1", shapes, [])),
        ],
    };

    private static string Root()
    {
        var directory = AppContext.BaseDirectory;

        while (directory is { Length: > 0 } && !Directory.Exists(Path.Combine(directory, "playground")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new DirectoryNotFoundException("The repository root is not above the test assembly.");
    }
}
