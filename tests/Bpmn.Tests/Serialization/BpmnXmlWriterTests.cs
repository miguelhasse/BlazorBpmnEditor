using System.Globalization;
using System.Text;
using System.Xml.Linq;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

/// <summary>
/// What the writer puts in the file, as distinct from what survives a round trip.
/// </summary>
/// <remarks>
/// A round trip can pass while the output is wrong in ways only another tool would notice —
/// children in the wrong order, a namespace never declared, a coordinate written with a decimal
/// comma. These assert the file itself.
/// </remarks>
public sealed class BpmnXmlWriterTests
{
    private static readonly XNamespace Model = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Diagram = "http://www.omg.org/spec/BPMN/20100524/DI";
    private static readonly XNamespace DiagramCommon = "http://www.omg.org/spec/DD/20100524/DC";
    private static readonly XNamespace DiagramInterchange = "http://www.omg.org/spec/DD/20100524/DI";
    private static readonly XNamespace Acme = "https://acme.example/schema/bpmn/1";

    // --- determinism ----------------------------------------------------------

    [Theory]
    [InlineData(BpmnFlowNodeType.SendTask)]
    [InlineData(BpmnFlowNodeType.ReceiveTask)]
    public void FidelityExportUsesChangedMessageTaskType(BpmnFlowNodeType type)
    {
        var document = BpmnXmlReader.Read("""
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:acme="urn:acme"
                         id="Definitions_1" targetNamespace="urn:test">
              <process id="Process_1">
                <userTask id="Task_1" acme:flag="keep"><documentation>Keep</documentation></userTask>
              </process>
            </definitions>
            """).Document;
        var task = document.FindFlowNode("Task_1")!;
        document = document with { FlowNodes = [task with { Type = type, MessageRef = "Message_1" }] };
        var saved = BpmnXmlReader.Read(BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity).Xml).Document;
        Assert.Equal(type, saved.FindFlowNode("Task_1")!.Type);
        Assert.Equal("Message_1", saved.FindFlowNode("Task_1")!.MessageRef);
        Assert.Equal("Keep", saved.FindFlowNode("Task_1")!.Extras.Documentation);
        Assert.Contains("acme:flag=\"keep\"", BpmnXmlWriter.Write(saved, BpmnWriteOptions.Fidelity).Xml, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalOutputIsByteIdenticalEveryTime()
    {
        var document = Sample();

        Assert.Equal(
            BpmnXmlWriter.Write(document).Xml,
            BpmnXmlWriter.Write(document).Xml);
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical, true)]
    [InlineData(BpmnWriteMode.Canonical, false)]
    [InlineData(BpmnWriteMode.Fidelity, true)]
    public void StreamOutputMatchesStringOutputAndLeavesStreamOpen(BpmnWriteMode mode, bool indent)
    {
        var document = InterchangeFixtures.Read("vendor-extensions.bpmn").Document;
        var options = new BpmnWriteOptions
        {
            Mode = mode,
            Indent = indent,
            AllowLossyCanonicalExport = true,
        };
        var expected = BpmnXmlWriter.Write(document, options);
        using var stream = new MemoryStream();

        var diagnostics = BpmnXmlWriter.Write(document, stream, options);

        var bytes = stream.ToArray();
        Assert.Equal(Encoding.UTF8.GetBytes(expected.Xml), bytes);
        Assert.Equal(expected.Diagnostics, diagnostics);
        Assert.Equal((byte)'<', bytes[0]);
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public void FidelityStreamOutputDoesNotAddAnAbsentXmlDeclaration()
    {
        var document = BpmnXmlReader.Read("""
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Definitions_1" targetNamespace="https://example.test">
              <process id="Process_1" />
            </definitions>
            """).Document;
        var expected = BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity);
        using var stream = new MemoryStream();

        BpmnXmlWriter.Write(document, stream, BpmnWriteOptions.Fidelity);

        var output = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Equal(expected.Xml, output);
        Assert.False(output.StartsWith("<?xml", StringComparison.Ordinal));
    }

    [Fact]
    public void CanonicalOutputDoesNotDependOnHowTheSourceWasFormatted()
    {
        // The same model reached two ways: read from a file, and read back from its own export.
        // Canonical mode is only a usable test oracle if those agree.
        var options = BpmnWriteOptions.Canonical with { AllowLossyCanonicalExport = true };
        var first = BpmnXmlWriter.Write(InterchangeFixtures.Read("vendor-extensions.bpmn").Document, options).Xml;
        var second = BpmnXmlWriter.Write(BpmnXmlReader.Read(first).Document, options).Xml;

        Assert.Equal(first, second);
    }

    [Fact]
    public void CanonicalOutputPreservesCalledElementQNameNamespaces()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:tns="https://example.test/target"
                         xmlns:ext="https://example.test/external"
                         id="Definitions_1" targetNamespace="https://example.test/target">
              <process id="Process_1" />
              <process id="Caller">
                <callActivity id="Call_local" calledElement="tns:Process_1" />
                <callActivity id="Call_external" calledElement="ext:Process_1" />
              </process>
            </definitions>
            """;
        var document = BpmnXmlReader.Read(xml).Document;

        var output = XDocument.Parse(BpmnXmlWriter.Write(document).Xml);
        var root = output.Root!;
        foreach (var (id, namespaceUri) in new[]
                 {
                     ("Call_local", "https://example.test/target"),
                     ("Call_external", "https://example.test/external"),
                 })
        {
            var call = Assert.Single(root.Descendants(Model + "callActivity"), element => element.Attribute("id")?.Value == id);
            var lexical = call.Attribute("calledElement")!.Value;
            var separator = lexical.IndexOf(':');
            Assert.True(separator > 0, lexical);
            Assert.Equal(namespaceUri, root.GetNamespaceOfPrefix(lexical[..separator])?.NamespaceName);
        }

        var reread = BpmnXmlReader.Read(output.ToString()).Document;
        Assert.Equal("Process_1", reread.FindFlowNode("Call_local")!.CalledElementRef);
        Assert.Null(reread.FindFlowNode("Call_external")!.CalledElementRef);
        Assert.Equal("https://example.test/external", reread.FindFlowNode("Call_external")!.CalledElementQName?.NamespaceUri);
    }

    [Fact]
    public void CanonicalOutputAvoidsQNamePrefixCollisionsWithTheBpmnNamespace()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:bpmn="https://example.test/external"
                         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                         id="Definitions_1" targetNamespace="https://example.test/target">
              <process id="Process_1">
                <callActivity id="Call_1" calledElement="bpmn:RemoteProcess" />
                <complexGateway id="Gateway_1">
                  <activationCondition>ready</activationCondition>
                </complexGateway>
              </process>
            </definitions>
            """;
        var document = BpmnXmlReader.Read(xml).Document;

        var output = XDocument.Parse(BpmnXmlWriter.Write(document).Xml);
        var root = output.Root!;
        var call = root.Descendants(Model + "callActivity").Single();
        var callQName = call.Attribute("calledElement")!.Value;
        Assert.Equal(
            "https://example.test/external",
            root.GetNamespaceOfPrefix(callQName[..callQName.IndexOf(':')])?.NamespaceName);

        var condition = root.Descendants(Model + "activationCondition").Single();
        var typeQName = condition.Attribute(XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance"))!.Value;
        Assert.Equal(
            Model.NamespaceName,
            root.GetNamespaceOfPrefix(typeQName[..typeQName.IndexOf(':')])?.NamespaceName);

        var reread = BpmnXmlReader.Read(output.ToString()).Document;
        Assert.Equal("https://example.test/external", reread.FindFlowNode("Call_1")!.CalledElementQName?.NamespaceUri);
        Assert.Equal(Model.NamespaceName, reread.FindFlowNode("Gateway_1")!.ActivationCondition?.TypeNamespaceUri);
    }

    [Fact]
    public void FidelityKeepsQNameIdentityWhenDescendantPrefixesShadowGeneratedPrefixes()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Definitions_1" targetNamespace="https://example.test/target">
              <process id="Process_1">
                <callActivity id="Call_1"
                              xmlns:local="urn:external"
                              xmlns:ns0="urn:other"
                              calledElement="local:Remote" />
              </process>
            </definitions>
            """;
        var document = BpmnXmlReader.Read(xml).Document;

        var output = BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity).Xml;
        var reread = BpmnXmlReader.Read(output).Document;

        Assert.Equal("urn:external", reread.FindFlowNode("Call_1")!.CalledElementQName?.NamespaceUri);
        Assert.Equal("Remote", reread.FindFlowNode("Call_1")!.CalledElementQName?.LocalName);
    }

    [Fact]
    public void FidelityRetainsUnmodeledAdHocCompletionCondition()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Definitions_1" targetNamespace="https://example.test/target">
              <process id="Process_1">
                <adHocSubProcess id="AdHoc_1">
                  <completionCondition>done</completionCondition>
                </adHocSubProcess>
              </process>
            </definitions>
            """;
        var document = BpmnXmlReader.Read(xml).Document;

        var output = BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity);
        var condition = Assert.Single(XDocument.Parse(output.Xml).Descendants(Model + "completionCondition"));

        Assert.Equal("done", condition.Value);
        Assert.True(output.IsLossless);
    }

    [Fact]
    public void FidelityRemovesClearedFormalExpressionLanguage()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                         id="Definitions_1" targetNamespace="https://example.test/target">
              <process id="Process_1">
                <task id="Task_1">
                  <standardLoopCharacteristics>
                    <loopCondition language="urn:old" xsi:type="bpmn:tFormalExpression">ready</loopCondition>
                  </standardLoopCharacteristics>
                </task>
              </process>
            </definitions>
            """;
        var document = BpmnXmlReader.Read(xml).Document;

        var edited = BpmnDocumentEditor.Apply(
            document,
            new UpdatePropertiesCommand("Task_1", "standardLoop.loopCondition.language", null));
        Assert.True(edited.IsApplied, string.Join("; ", edited.Rejections.Select(rejection => rejection.Message)));

        var output = BpmnXmlWriter.Write(edited.Document, BpmnWriteOptions.Fidelity);
        var condition = Assert.Single(XDocument.Parse(output.Xml).Descendants(Model + "loopCondition"));

        Assert.Null(condition.Attribute("language"));
        Assert.Equal("ready", condition.Value);
        Assert.Null(BpmnXmlReader.Read(output.Xml).Document.FindFlowNode("Task_1")!.StandardLoop?.LoopCondition?.Language);
    }

    [Fact]
    public void CanonicalOutputRetainsLoopGatewayAndAssignmentSemantics()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                         id="Definitions_1" targetNamespace="https://example.test/target">
              <process id="Process_1">
                <task id="Task_standard">
                  <standardLoopCharacteristics testBefore="false" loopMaximum="8">
                    <loopCondition language="urn:expr" xsi:type="bpmn:tFormalExpression">count &lt; 8</loopCondition>
                  </standardLoopCharacteristics>
                </task>
                <task id="Task_multi">
                  <multiInstanceLoopCharacteristics isSequential="true" behavior="Complex">
                    <loopCardinality xsi:type="bpmn:tFormalExpression">4</loopCardinality>
                    <loopDataInputRef>Input_1</loopDataInputRef>
                    <inputDataItem id="InputItem_1" name="item" itemSubjectRef="Item_1" isCollection="false" />
                    <loopDataOutputRef>Output_1</loopDataOutputRef>
                    <outputDataItem id="OutputItem_1" name="result" itemSubjectRef="Item_2" />
                    <complexBehaviorDefinition event="Exclusive">
                      <condition xsi:type="bpmn:tFormalExpression">done</condition>
                    </complexBehaviorDefinition>
                    <completionCondition xsi:type="bpmn:tFormalExpression">completed &gt; 0</completionCondition>
                  </multiInstanceLoopCharacteristics>
                  <dataInputAssociation id="Assignment_1">
                    <sourceRef>Data_1</sourceRef>
                    <targetRef>Input_1</targetRef>
                    <assignment>
                      <from xsi:type="bpmn:tFormalExpression">source.value</from>
                      <to xsi:type="bpmn:tFormalExpression">target.value</to>
                    </assignment>
                  </dataInputAssociation>
                </task>
                <complexGateway id="Gateway_complex">
                  <activationCondition xsi:type="bpmn:tFormalExpression">ready</activationCondition>
                </complexGateway>
              </process>
            </definitions>
            """;
        var document = BpmnXmlReader.Read(xml).Document;

        var output = BpmnXmlWriter.Write(document).Xml;
        var reread = BpmnXmlReader.Read(output).Document;

        var standard = reread.FindFlowNode("Task_standard")!;
        Assert.Equal(8, standard.StandardLoop?.LoopMaximum);
        Assert.Equal("count < 8", standard.StandardLoop?.LoopCondition?.Body);

        var multi = reread.FindFlowNode("Task_multi")!.MultiInstanceLoop!;
        Assert.Equal("4", multi.LoopCardinality?.Body);
        Assert.Equal("InputItem_1", multi.InputDataItem?.Id);
        Assert.Equal("completed > 0", multi.CompletionCondition?.Body);
        Assert.Equal("done", multi.ComplexBehaviorDefinitions?.Single().Condition?.Body);
        Assert.Equal("ready", reread.FindFlowNode("Gateway_complex")?.ActivationCondition?.Body);
        var assignment = Assert.Single(reread.DataAssociations).Assignments.Single();
        Assert.Equal("source.value", assignment.From.Body);
        Assert.Equal("target.value", assignment.To.Body);
    }

    [Fact]
    public void WritesCoordinatesInInvariantCulture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            // A decimal comma in dc:Bounds/@x would make the file unreadable everywhere else,
            // and the machine that wrote it is the one machine that would not notice.
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            var document = new BpmnDocumentBuilder("Process_1")
                .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(10.5, 20.25, 100, 80), "Task")
                .Build();

            var bounds = Assert.Single(Export(document).Descendants(DiagramCommon + "Bounds"));

            Assert.Equal("10.5", bounds.Attribute("x")?.Value);
            Assert.Equal("20.25", bounds.Attribute("y")?.Value);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void WritesWholeCoordinatesWithoutADecimalPoint()
    {
        var shape = Export(Sample())
            .Descendants(Diagram + "BPMNShape")
            .Single(element => element.Attribute("bpmnElement")?.Value == "Task_1");

        var bounds = Assert.Single(shape.Elements(DiagramCommon + "Bounds"));

        Assert.Equal("200", bounds.Attribute("x")?.Value);
        Assert.Equal("80", bounds.Attribute("height")?.Value);
    }

    [Fact]
    public void DeclaresUtf8InTheConventionalSpelling() =>
        Assert.StartsWith(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
            BpmnXmlWriter.Write(Sample()).Xml,
            StringComparison.Ordinal);

    // --- schema order ---------------------------------------------------------

    [Fact]
    public void WritesLaneSetsBeforeFlowElements()
    {
        // tProcess is an xsd:sequence: laneSet* comes before flowElement*, and a document with
        // them the other way round is rejected by conforming tools even though nothing is missing.
        var document = new BpmnDocumentBuilder("Process_1")
            .Pool("Participant_1", "Pool", new Bounds(0, 0, 600, 250))
            .Lane("Lane_1", "Lane", new Bounds(30, 0, 570, 250), ["Task_1"])
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 80, 100, 80), "Task")
            .Build();

        var process = Assert.Single(Export(document).Descendants(Model + "process"));
        var names = process.Elements().Select(x => x.Name.LocalName).ToList();

        Assert.True(names.IndexOf("laneSet") < names.IndexOf("task"), string.Join(", ", names));
    }

    [Fact]
    public void WritesExtensionElementsBeforeEventDefinitions()
    {
        var process = Assert.Single(
            InterchangeFixtures.Export("vendor-extensions.bpmn", BpmnWriteMode.Canonical)
                .Descendants(Model + "serviceTask"));

        var names = process.Elements().Select(x => x.Name.LocalName).ToList();

        Assert.True(
            names.IndexOf("documentation") < names.IndexOf("extensionElements"),
            string.Join(", ", names));
        Assert.True(
            names.IndexOf("extensionElements") < names.IndexOf("multiInstanceLoopCharacteristics"),
            string.Join(", ", names));
    }

    [Fact]
    public void WritesBoundsBeforeTheLabel()
    {
        var shape = InterchangeFixtures.Export("vendor-extensions.bpmn", BpmnWriteMode.Canonical)
            .Descendants(Diagram + "BPMNShape")
            .Single(x => x.Attribute("id")?.Value == "Shape_Start");

        var names = shape.Elements().Select(x => x.Name.LocalName).ToList();

        Assert.Equal(["extension", "Bounds", "BPMNLabel"], names);
    }

    [Fact]
    public void CanonicalKeepsTheOrderFlowElementsWereWrittenIn()
    {
        // flowElement* is one xsd:sequence slot holding a substitution group, so ranking a Start
        // Event above a Task would be just as schema-valid — and would reorder every document this
        // writer touches, turning a one-line edit into a whole-file diff.
        var process = Assert.Single(
            InterchangeFixtures.Export("vendor-extensions.bpmn", BpmnWriteMode.Canonical)
                .Descendants(Model + "process"));

        Assert.Equal(
            ["documentation", "extensionElements", "startEvent", "serviceTask", "endEvent", "sequenceFlow", "sequenceFlow"],
            process.Elements().Select(x => x.Name.LocalName));
    }

    [Fact]
    public void CanonicalWritesRootElementsBeforeTheDiagram()
    {
        // tDefinitions is import*, extension*, rootElement*, BPMNDiagram*, relationship*. Content
        // this editor preserves without modelling — Choreography, vendor roots — is still a root
        // element, so writing it after the diagram breaks the sequence.
        var names = InterchangeFixtures.Export("vendor-extensions.bpmn", BpmnWriteMode.Canonical)
            .Root!
            .Elements()
            .Select(x => x.Name.LocalName)
            .ToList();

        Assert.Equal(
            ["import", "extension", "process", "choreography", "catalogue", "BPMNDiagram"],
            names);
    }

    [Fact]
    public void WritesASubProcessActivityChildrenBeforeItsContent()
    {
        // A Sub-Process is an Activity first: tSubProcess extends tActivity, so incoming, outgoing
        // and loopCharacteristics all precede the flow elements it contains. Reusing the Process
        // order here would put them after, which is the easy mistake.
        var options = new BpmnWriteOptions
        {
            WriteFlowNodeReferences = true,
            AllowLossyCanonicalExport = true,
        };
        var xml = BpmnXmlWriter.Write(InterchangeFixtures.Read("nested-subprocesses.bpmn").Document, options).Xml;

        var subProcess = XDocument.Parse(xml).Descendants(Model + "subProcess").First();
        var names = subProcess.Elements().Select(x => x.Name.LocalName).ToList();

        Assert.Contains("incoming", names);
        Assert.True(
            names.LastIndexOf("outgoing") < names.IndexOf("startEvent"),
            string.Join(", ", names));
    }

    // --- namespaces -----------------------------------------------------------

    [Fact]
    public void DeclaresEveryNamespaceItUses()
    {
        var root = InterchangeFixtures.Export("vendor-extensions.bpmn", BpmnWriteMode.Canonical).Root!;

        var used = root.DescendantsAndSelf()
            .SelectMany(element => element.Attributes()
                .Where(attribute => !attribute.IsNamespaceDeclaration)
                .Select(attribute => attribute.Name.NamespaceName)
                .Append(element.Name.NamespaceName))
            .Where(ns => ns.Length > 0)
            .Distinct(StringComparer.Ordinal);

        foreach (var ns in used)
        {
            Assert.False(
                string.IsNullOrEmpty(root.GetPrefixOfNamespace(ns)) && ns != root.Name.NamespaceName,
                $"Namespace '{ns}' is used but has no prefix bound on the root.");
        }
    }

    [Fact]
    public void DoesNotDeclareNamespacesTheDocumentNeverUses()
    {
        // zeebe-process.bpmn declares the DI namespace but draws no edges, so nothing in the
        // export references it. Declaring it anyway is noise in every consumer's diff.
        var root = InterchangeFixtures.Export("zeebe-extensions.bpmn", BpmnWriteMode.Canonical).Root!;
        var declared = root.Attributes()
            .Where(x => x.IsNamespaceDeclaration)
            .Select(x => x.Value)
            .ToList();

        Assert.DoesNotContain(DiagramInterchange.NamespaceName, declared, StringComparer.Ordinal);
    }

    [Fact]
    public void ReusesThePrefixesTheDocumentWasWrittenWith()
    {
        var root = InterchangeFixtures.Export("vendor-extensions.bpmn", BpmnWriteMode.Canonical).Root!;

        Assert.Equal("bpmn", root.GetPrefixOfNamespace(Model));
        Assert.Equal("acme", root.GetPrefixOfNamespace(Acme));
    }

    // --- preservation ---------------------------------------------------------

    [Fact]
    public void FidelityKeepsCommentsAndProcessingInstructions()
    {
        var xml = BpmnXmlWriter.Write(
            InterchangeFixtures.Read("vendor-extensions.bpmn").Document,
            BpmnWriteOptions.Fidelity).Xml;

        Assert.Contains("A hand-built interchange fixture", xml, StringComparison.Ordinal);
        Assert.Contains("<?bpmn-fixture purpose=\"round-trip\"?>", xml, StringComparison.Ordinal);
        Assert.Contains("Out of scope for rendering", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalDropsCommentsBecauseTheModelCannotHoldThem()
    {
        var document = InterchangeFixtures.Read("vendor-extensions.bpmn").Document;

        var refused = Assert.Throws<BpmnLossyExportException>(() => BpmnXmlWriter.Write(document));
        Assert.Contains(refused.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("comment", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(refused.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("ioSpecification", StringComparison.Ordinal));

        // Explicit opt-in is required before this known lossy mode writes anything.
        var xml = BpmnXmlWriter.Write(
            document,
            BpmnWriteOptions.Canonical with { AllowLossyCanonicalExport = true }).Xml;

        Assert.DoesNotContain("A hand-built interchange fixture", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalExportOfANewDocumentDoesNotRequireLossOptIn()
    {
        var result = BpmnXmlWriter.Write(Sample());

        Assert.Equal(BpmnWriteMode.Canonical, result.Mode);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == BpmnExportSeverity.Dropped);
    }

    [Fact]
    public void FidelityKeepsElementsTheModelNeverRead()
    {
        var root = InterchangeFixtures.Export("vendor-extensions.bpmn", BpmnWriteMode.Fidelity).Root!;

        // ioSpecification carries an id and sits among elements the writer owns. Treating it as
        // owned would delete it on every save.
        Assert.Single(root.Descendants(Model + "ioSpecification"), x => x.Attribute("id")?.Value == "IoSpec_Work");
        Assert.Single(root.Descendants(Model + "inputSet"));
    }

    [Fact]
    public void FidelityKeepsTheIdOfAnElementTheModelStoresWithoutOne()
    {
        // The loop marker is an enum in the model, so its element's id has nowhere to live —
        // it survives because fidelity mode updates that element in place instead of rebuilding it.
        var loop = Assert.Single(
            InterchangeFixtures.Export("vendor-extensions.bpmn", BpmnWriteMode.Fidelity)
                .Descendants(Model + "multiInstanceLoopCharacteristics"));

        Assert.Equal("Loop_Work", loop.Attribute("id")?.Value);
        Assert.Equal("true", loop.Attribute("isSequential")?.Value);
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void KeepsOutOfScopeChoreographyContent(BpmnWriteMode mode)
    {
        var root = InterchangeFixtures.Export("vendor-extensions.bpmn", mode).Root!;

        Assert.Single(root.Elements(Model + "choreography"));
        Assert.Single(root.Descendants(Model + "choreographyTask"));
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void KeepsForeignRootElements(BpmnWriteMode mode)
    {
        var root = InterchangeFixtures.Export("vendor-extensions.bpmn", mode).Root!;
        var catalogue = Assert.Single(root.Elements(Acme + "catalogue"));

        Assert.Equal("emea", Assert.Single(catalogue.Elements(Acme + "entry")).Value);
    }

    [Fact]
    public void FidelityDropsClearedForeignAttributesButKeepsUntouchedAttributes()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                         xmlns:vendor="urn:vendor"
                         id="Definitions" targetNamespace="urn:test">
              <process id="Process">
                <serviceTask id="Task" zeebe:modelerTemplate="x" zeebe:modelerTemplateVersion="1" vendor:flag="keep" />
              </process>
            </definitions>
            """;
        var document = BpmnXmlReader.Read(xml).Document;
        var unchanged = BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity).Xml;
        var unchangedTask = Assert.Single(XDocument.Parse(unchanged).Descendants(Model + "serviceTask"));
        Assert.Equal("x", unchangedTask.Attribute(XName.Get("modelerTemplate", "http://camunda.org/schema/zeebe/1.0"))?.Value);
        Assert.Equal("1", unchangedTask.Attribute(XName.Get("modelerTemplateVersion", "http://camunda.org/schema/zeebe/1.0"))?.Value);
        Assert.Equal("keep", unchangedTask.Attribute(XNamespace.Get("urn:vendor") + "flag")?.Value);
        Assert.Equal(
            document.FindFlowNode("Task")!.Extras.ForeignAttributes.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            BpmnXmlReader.Read(unchanged).Document.FindFlowNode("Task")!.Extras.ForeignAttributes.OrderBy(pair => pair.Key, StringComparer.Ordinal));

        var unlinked = BpmnDocumentEditor.Apply(document, new UnlinkElementTemplateCommand("Task")).Document;
        var output = XDocument.Parse(BpmnXmlWriter.Write(unlinked, BpmnWriteOptions.Fidelity).Xml);
        var task = Assert.Single(output.Descendants(Model + "serviceTask"));

        Assert.Null(task.Attribute(XName.Get("modelerTemplate", "http://camunda.org/schema/zeebe/1.0")));
        Assert.Null(task.Attribute(XName.Get("modelerTemplateVersion", "http://camunda.org/schema/zeebe/1.0")));
        Assert.Equal("keep", task.Attribute(XNamespace.Get("urn:vendor") + "flag")?.Value);
    }

    [Fact]
    public void FidelityKeepsForeignAttributesOnElementsTheModelDoesNotCapture()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                         xmlns:vendor="urn:vendor"
                         id="Definitions" targetNamespace="urn:test">
              <process id="Process">
                <startEvent id="Start" />
                <endEvent id="End" />
                <sequenceFlow id="Flow" sourceRef="Start" targetRef="End">
                  <conditionExpression xsi:type="tFormalExpression" vendor:flag="keep">x</conditionExpression>
                </sequenceFlow>
              </process>
            </definitions>
            """;
        var document = BpmnXmlReader.Read(xml).Document;
        var output = XDocument.Parse(BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity).Xml);
        var condition = Assert.Single(output.Descendants(Model + "conditionExpression"));

        Assert.Equal("keep", condition.Attribute(XNamespace.Get("urn:vendor") + "flag")?.Value);
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void KeepsForeignAttributesOnKnownElements(BpmnWriteMode mode)
    {
        var task = Assert.Single(
            InterchangeFixtures.Export("vendor-extensions.bpmn", mode).Descendants(Model + "serviceTask"));

        Assert.Equal("TRK-9", task.Attribute(Acme + "trackingId")?.Value);
        Assert.Equal(
            "acme.template.v2",
            task.Attribute(XName.Get("{http://camunda.org/schema/zeebe/1.0}modelerTemplate"))?.Value);
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void KeepsNestedExtensionContentWithItsQualifiedAttributesAndText(BpmnWriteMode mode)
    {
        var routing = Assert.Single(
            InterchangeFixtures.Export("vendor-extensions.bpmn", mode).Descendants(Acme + "routing"));

        var rules = routing.Elements(Acme + "rule").ToList();

        Assert.Equal(2, rules.Count);
        Assert.Equal("1", rules[0].Attribute(Acme + "priority")?.Value);
        Assert.Equal("amount > 100", rules[0].Attribute("match")?.Value);
        Assert.Equal("escalate", rules[0].Value);
        Assert.Equal("continue", rules[1].Value);
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void KeepsDiagramExtensionContent(BpmnWriteMode mode)
    {
        var extension = Assert.Single(
            InterchangeFixtures.Export("vendor-extensions.bpmn", mode)
                .Descendants(DiagramInterchange + "extension"));

        Assert.Equal("#336699", Assert.Single(extension.Elements(Acme + "style")).Attribute("stroke")?.Value);
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void KeepsLabelStyles(BpmnWriteMode mode)
    {
        var style = Assert.Single(
            InterchangeFixtures.Export("vendor-extensions.bpmn", mode).Descendants(Diagram + "BPMNLabelStyle"));

        var font = Assert.Single(style.Elements(DiagramCommon + "Font"));

        Assert.Equal("LabelStyle_1", style.Attribute("id")?.Value);
        Assert.Equal("Arial", font.Attribute("name")?.Value);
        Assert.Equal("11", font.Attribute("size")?.Value);
        Assert.Equal("true", font.Attribute("isBold")?.Value);
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void KeepsDocumentLevelExtensionDeclarationsAndImports(BpmnWriteMode mode)
    {
        var root = InterchangeFixtures.Export("vendor-extensions.bpmn", mode).Root!;

        Assert.Single(root.Elements(Model + "import"));
        var declaration = Assert.Single(root.Elements(Model + "extension"));

        Assert.Equal("acme:trackingProfile", declaration.Attribute("definition")?.Value);
        Assert.Single(declaration.Descendants(Acme + "field"));
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void KeepsTheTimerExpressionKind(BpmnWriteMode mode)
    {
        // timeDate, timeDuration and timeCycle mean three different things; rewriting one as
        // another silently changes when the Event fires.
        var timer = Assert.Single(
            InterchangeFixtures.Export("global-catalog.bpmn", mode).Descendants(Model + "timerEventDefinition"));

        Assert.Equal("PT30M", Assert.Single(timer.Elements(Model + "timeDuration")).Value);
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void KeepsTheDefaultFlowOnItsSource(BpmnWriteMode mode)
    {
        var gateway = Assert.Single(
            InterchangeFixtures.Export("global-catalog.bpmn", mode).Descendants(Model + "exclusiveGateway"));

        Assert.Equal("Flow_GatewayToApprove", gateway.Attribute("default")?.Value);
    }

    // --- modes and diagnostics ------------------------------------------------

    [Fact]
    public void FidelityFallsBackToCanonicalForADocumentThatWasNeverRead()
    {
        var result = BpmnXmlWriter.Write(Sample(), BpmnWriteOptions.Fidelity);

        Assert.Equal(BpmnWriteMode.Canonical, result.Mode);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains("not read from XML", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsTheModeItActuallyUsed()
    {
        var document = InterchangeFixtures.Read("simple-process.bpmn").Document;

        Assert.Equal(BpmnWriteMode.Fidelity, BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity).Mode);
        Assert.Equal(BpmnWriteMode.Canonical, BpmnXmlWriter.Write(document, BpmnWriteOptions.Canonical).Mode);
    }

    [Fact]
    public void RemovingAnElementFromTheModelRemovesItFromFidelityOutput()
    {
        var document = InterchangeFixtures.Read("vendor-extensions.bpmn").Document;
        var trimmed = document with
        {
            FlowNodes = document.FlowNodes.Where(node => node.Id != "Task_Work").ToList(),
        };

        var result = BpmnXmlWriter.Write(trimmed, BpmnWriteOptions.Fidelity);
        var root = XDocument.Parse(result.Xml).Root!;

        Assert.Empty(root.Descendants(Model + "serviceTask"));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.ElementId == "Task_Work");

        // Its vendor content goes with it; nothing is left orphaned in the file.
        Assert.Empty(root.Descendants(Acme + "routing"));
    }

    [Fact]
    public void MovingAShapeChangesOnlyItsBounds()
    {
        var document = InterchangeFixtures.Read("vendor-extensions.bpmn").Document;
        var diagram = document.Diagrams[0];
        var moved = diagram.Plane.Shapes
            .Select(shape => shape.Id == "Shape_Work" ? shape with { Bounds = new Bounds(500, 300, 120, 90) } : shape)
            .ToList();

        var edited = document with
        {
            Diagrams = [diagram with { Plane = diagram.Plane with { Shapes = moved } }],
        };

        var before = BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity).Xml;
        var after = BpmnXmlWriter.Write(edited, BpmnWriteOptions.Fidelity).Xml;

        var changed = before.Split('\n')
            .Zip(after.Split('\n'))
            .Where(pair => pair.First != pair.Second)
            .ToList();

        var (First, Second) = Assert.Single(changed);
        Assert.Contains("x=\"500\"", Second, StringComparison.Ordinal);
    }

    [Fact]
    public void FidelityKeepsUnchangedFormalExpressionMixedContent()
    {
        var document = ExpressionDocument();

        var condition = Assert.Single(
            XDocument.Parse(BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity).Xml)
                .Descendants(Model + "loopCondition"));

        Assert.Equal("count < legacy", condition.Value);
        Assert.Equal("legacy", condition.Element(Acme + "part")?.Value);
    }

    [Fact]
    public void FidelityReplacesMixedContentWhenFormalExpressionBodyChanges()
    {
        var document = ExpressionDocument();
        var task = document.FindFlowNode("Task_1")!;
        var editedLoop = task.StandardLoop! with
        {
            LoopCondition = task.StandardLoop.LoopCondition! with { Body = "count < 9" },
        };
        var edited = document with
        {
            FlowNodes = document.FlowNodes
                .Select(node => node.Id == task.Id ? node with { StandardLoop = editedLoop } : node)
                .ToList(),
        };

        var condition = Assert.Single(
            XDocument.Parse(BpmnXmlWriter.Write(edited, BpmnWriteOptions.Fidelity).Xml)
                .Descendants(Model + "loopCondition"));

        Assert.Equal("count < 9", condition.Value);
        Assert.Empty(condition.Elements());
        Assert.Equal("urn:expression", condition.Attribute("language")?.Value);
        Assert.Equal(
            "bpmn:tFormalExpression",
            condition.Attribute(XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance"))?.Value);
    }

    [Fact]
    public void FidelityReportsWhenAnEditedFormalExpressionReplacesNestedMarkup()
    {
        var document = ExpressionDocument();
        var task = document.FindFlowNode("Task_1")!;
        var edited = document with
        {
            FlowNodes = document.FlowNodes
                .Select(node => node.Id == task.Id
                    ? node with
                    {
                        StandardLoop = task.StandardLoop! with
                        {
                            LoopCondition = task.StandardLoop.LoopCondition! with { Body = "count < 9" },
                        },
                    }
                    : node)
                .ToList(),
        };

        var result = BpmnXmlWriter.Write(edited, BpmnWriteOptions.Fidelity);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == BpmnExportSeverity.Normalized
            && diagnostic.ElementId == "Task_1"
            && diagnostic.Message.Contains("nested XML content", StringComparison.Ordinal));
    }

    [Fact]
    public void FidelityUpdatesEveryModeledTextLeaf()
    {
        var document = TextLeafDocument();
        var changed = document with
        {
            Lanes = document.Lanes
                .Select(lane => lane with { FlowNodeRefs = ["Start_1"] })
                .ToList(),
            DataAssociations = document.DataAssociations
                .Select(association => association with
                {
                    SourceRefs = ["Data_2"],
                    TargetRef = "Input_2",
                })
                .ToList(),
            FlowNodes = document.FlowNodes
                .Select(node => node.Id == "Start_1"
                    ? node with
                    {
                        EventDefinitions = node.EventDefinitions
                            .Select(definition => definition with { Expression = "PT2H" })
                            .ToList(),
                    }
                    : node)
                .ToList(),
            TextAnnotations = document.TextAnnotations
                .Select(annotation => annotation with { Text = "after" })
                .ToList(),
        };

        var result = BpmnXmlWriter.Write(changed, BpmnWriteOptions.Fidelity);
        var root = XDocument.Parse(result.Xml).Root!;

        Assert.Equal("Start_1", Assert.Single(root.Descendants(Model + "flowNodeRef")).Value);
        Assert.Equal("Data_2", Assert.Single(root.Descendants(Model + "sourceRef")).Value);
        Assert.Equal("Input_2", Assert.Single(root.Descendants(Model + "targetRef")).Value);
        Assert.Equal("PT2H", Assert.Single(root.Descendants(Model + "timeDuration")).Value);
        Assert.Equal("after", Assert.Single(root.Descendants(Model + "text")).Value);
    }

    [Fact]
    public void WritesAnEventSubProcessAsATriggeredSubProcess()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Sub_1", BpmnFlowNodeType.EventSubProcess, new Bounds(0, 0, 350, 200), "Handler")
            .Build();

        var element = Assert.Single(Export(document).Descendants(Model + "subProcess"));

        Assert.Equal("true", element.Attribute("triggeredByEvent")?.Value);
    }

    [Fact]
    public void OmitsFlowNodeReferencesUnlessAsked()
    {
        var document = Sample();

        Assert.Empty(Export(document).Descendants(Model + "outgoing"));
        Assert.NotEmpty(
            XDocument.Parse(BpmnXmlWriter.Write(document, new BpmnWriteOptions { WriteFlowNodeReferences = true }).Xml)
                .Descendants(Model + "outgoing"));
    }

    [Fact]
    public void StampsTheExporterWhenAsked()
    {
        var options = new BpmnWriteOptions { Exporter = "Hasseware BPMN", ExporterVersion = "2.0" };
        var root = XDocument.Parse(BpmnXmlWriter.Write(Sample(), options).Xml).Root!;

        Assert.Equal("Hasseware BPMN", root.Attribute("exporter")?.Value);
        Assert.Equal("2.0", root.Attribute("exporterVersion")?.Value);
    }

    private static XDocument Export(BpmnDocument document) => XDocument.Parse(BpmnXmlWriter.Write(document).Xml);

    private static BpmnDocument ExpressionDocument() => BpmnXmlReader.Read("""
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                     xmlns:acme="https://acme.example/schema/bpmn/1"
                     id="Definitions_1" targetNamespace="https://example.test">
          <process id="Process_1">
            <task id="Task_1">
              <standardLoopCharacteristics>
                <loopCondition language="urn:expression" xsi:type="bpmn:tFormalExpression">count &lt; <acme:part>legacy</acme:part></loopCondition>
              </standardLoopCharacteristics>
            </task>
          </process>
        </definitions>
        """).Document;

    private static BpmnDocument TextLeafDocument() => BpmnXmlReader.Read("""
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     id="Definitions_1" targetNamespace="https://example.test">
          <process id="Process_1">
            <laneSet id="LaneSet_1">
              <lane id="Lane_1"><flowNodeRef>Task_1</flowNodeRef></lane>
            </laneSet>
            <startEvent id="Start_1">
              <timerEventDefinition><timeDuration>PT1H</timeDuration></timerEventDefinition>
            </startEvent>
            <task id="Task_1">
              <dataInputAssociation id="DataAssociation_1">
                <sourceRef>Data_1</sourceRef>
                <targetRef>Input_1</targetRef>
              </dataInputAssociation>
            </task>
            <textAnnotation id="Annotation_1"><text>before</text></textAnnotation>
          </process>
        </definitions>
        """).Document;

    private static BpmnDocument Sample() => new BpmnDocumentBuilder("Process_1", "Sample")
        .Node("Event_1", BpmnFlowNodeType.StartEvent, new Bounds(100, 100, 36, 36), "Start")
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(200, 80, 100, 80), "Work")
        .Flow("Flow_1", "Event_1", "Task_1", [new DiPoint(136, 118), new DiPoint(200, 118)])
        .Build();
}
