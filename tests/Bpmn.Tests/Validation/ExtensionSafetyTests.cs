using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Validation;

/// <summary>
/// Active markup hidden in preserved extension content (implementation plan M7).
/// </summary>
/// <remarks>
/// Two requirements that pull against each other: section 6.1 says unknown vendor XML round-trips
/// untouched, section 5.5 says unsafe external content is quarantined. The content is therefore
/// never rewritten and the document is reported non-exportable instead — and the hard part is not
/// catching the dangerous cases but leaving the innocent ones alone, because a false positive here
/// makes a legitimate document unusable.
/// </remarks>
public sealed class ExtensionSafetyTests
{
    private const string Svg = "http://www.w3.org/2000/svg";
    private const string Html = "http://www.w3.org/1999/xhtml";
    private const string Zeebe = "http://camunda.org/schema/zeebe/1.0";

    // --- what must be caught -----------------------------------------------------------

    [Fact]
    public void ScriptInTheSvgNamespaceIsUnsafe()
    {
        var found = Scan(new BpmnExtensionElement(Svg, "script", Empty, [], "alert(1)"));

        var problem = Assert.Single(found);
        Assert.Contains("active markup", problem.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("iframe")]
    [InlineData("object")]
    [InlineData("embed")]
    [InlineData("foreignObject")]
    public void SoIsAnythingElseThatRunsOrLoads(string localName)
    {
        Assert.Single(Scan(new BpmnExtensionElement(Svg, localName, Empty, [])));
    }

    [Fact]
    public void AnEventHandlerInsideActiveMarkupIsUnsafe()
    {
        var found = Scan(new BpmnExtensionElement(Html, "div", Attributes(("onclick", "steal()")), []));

        var problem = Assert.Single(found);
        Assert.Contains("event handler", problem.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("  java\tscript:alert(1)")]
    [InlineData("vbscript:msgbox")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    public void AScriptUrlIsUnsafeWhereverItAppears(string url)
    {
        /*
         * Including in a vendor's own namespace, and including when it is spelled with whitespace
         * or control characters inside the scheme — which a browser honours and a naive string
         * comparison does not.
         */
        Assert.Single(Scan(new BpmnExtensionElement(Zeebe, "taskDefinition", Attributes(("type", url)), [])));
    }

    [Fact]
    public void NestedContentIsReachedAndNamed()
    {
        var document = Document(new BpmnExtensionElement(
            Zeebe,
            "properties",
            Empty,
            [new BpmnExtensionElement(Zeebe, "property", Empty, [new BpmnExtensionElement(Svg, "script", Empty, [])])]));

        var problem = Assert.Single(BpmnExtensionSafety.Scan(document));

        // The path has to say where it is, or a reader cannot find it in a large extension tree.
        Assert.Equal("properties/property/script", problem.Path);
        Assert.Equal("Task_1", problem.ElementId);
    }

    [Fact]
    public void AForeignAttributeCarryingAScriptUrlIsUnsafe()
    {
        var document = Document() with
        {
            FlowNodes =
            [
                new BpmnFlowNode("Task_1", BpmnFlowNodeType.Task, "Process_1")
                {
                    Extras = BpmnElementExtras.None with
                    {
                        ForeignAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["{http://example.com}href"] = "javascript:alert(1)",
                        },
                    },
                },
            ],
        };

        Assert.Single(BpmnExtensionSafety.Scan(document));
    }

    // --- what must be left alone -------------------------------------------------------

    [Fact]
    public void AVendorScriptElementIsNotActiveMarkup()
    {
        /*
         * The false positive this is written to avoid. Zeebe, Camunda and BPMN itself all have a
         * `script` element, none of them executes anything, and quarantining by local name would
         * make half the documents in the wild unusable.
         */
        Assert.Empty(Scan(new BpmnExtensionElement(Zeebe, "script", Empty, [], "= a + b")));
    }

    [Fact]
    public void AVendorAttributeThatMerelyStartsWithOnIsNotAHandler()
    {
        Assert.Empty(Scan(new BpmnExtensionElement(Zeebe, "form", Attributes(("onboarding", "true")), [])));
    }

    [Fact]
    public void OrdinaryHtmlInDocumentationIsNotQuarantined()
    {
        // Preserved markup is not the danger; markup that runs is. A vendor embedding formatted
        // text is the ordinary case and must stay exportable.
        Assert.Empty(Scan(new BpmnExtensionElement(Html, "p", Empty, [], "Approve within 24 hours.")));
    }

    [Fact]
    public void AnOrdinaryUrlIsNotAScriptUrl()
    {
        Assert.Empty(Scan(new BpmnExtensionElement(Zeebe, "link", Attributes(("href", "https://example.com/spec")), [])));
        Assert.Empty(Scan(new BpmnExtensionElement(Zeebe, "icon", Attributes(("src", "data:image/png;base64,iVBOR")), [])));
    }

    [Fact]
    public void NoShippedFixtureIsQuarantined()
    {
        // The regression that matters: every sample must stay exportable, or this rule has made
        // the product worse rather than safer.
        foreach (var sample in Directory.GetFiles(Samples(), "*.bpmn"))
        {
            var document = BpmnXmlReader.Read(File.ReadAllText(sample)).Document;

            Assert.Empty(BpmnExtensionSafety.Scan(document));
        }
    }

    // --- helpers -----------------------------------------------------------------------

    private static IReadOnlyDictionary<string, string> Empty => new Dictionary<string, string>(StringComparer.Ordinal);

    private static Dictionary<string, string> Attributes(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

    private static IReadOnlyList<BpmnUnsafeContent> Scan(BpmnExtensionElement extension) =>
        BpmnExtensionSafety.Scan(Document(extension));

    private static BpmnDocument Document(params BpmnExtensionElement[] extensions)
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80), "Work")
            .Build();

        return document with
        {
            FlowNodes =
            [
                document.FlowNodes[0] with
                {
                    Extras = BpmnElementExtras.None with { Extensions = extensions },
                },
            ],
        };
    }

    private static string Samples()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "playground")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("the repository root was not found");

        return Path.Combine(root, "playground", "Bpmn.Demo", "wwwroot", "samples");
    }
}
