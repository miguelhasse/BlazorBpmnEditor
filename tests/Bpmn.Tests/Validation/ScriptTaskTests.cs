using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Scripting.CSharp;
using Hasseware.AspNetCore.Components.Bpmn.Scripting.JavaScript;
using Hasseware.AspNetCore.Components.Bpmn.Scripting.Lua;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Validation;

/// <summary>
/// Script Task validation: what the model keeps of a script, what the core profile asks of it, and
/// what each language backend says about its syntax.
/// </summary>
/// <remarks>
/// The profile is the engine-neutral half and the backends are the per-language half, so the
/// profile is exercised against a stub that records what it was asked and the backends are
/// exercised directly. The two meet once, in the end-to-end case.
/// </remarks>
public sealed class ScriptTaskTests
{
    private const string Header = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Defs" targetNamespace="urn:test">
          <bpmn:process id="Process_1" isExecutable="true">
        """;

    private const string Footer = """
          </bpmn:process>
        </bpmn:definitions>
        """;

    // --- model and serialization -------------------------------------------------------

    [Fact]
    public void TheReaderKeepsTheFormatAndTheBody()
    {
        var task = ReadTask("javascript", "var x = 1;\nreturn x;");

        Assert.Equal("javascript", task.ScriptFormat);
        Assert.Equal("var x = 1;\nreturn x;", task.Script);
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void BothWritersRoundTripThem(BpmnWriteMode mode)
    {
        var imported = BpmnXmlReader.Read(Xml("lua", "return 1 < 2"));
        var written = BpmnXmlWriter.Write(imported.Document, new BpmnWriteOptions { Mode = mode });
        var task = Task(BpmnXmlReader.Read(written.Xml).Document);

        Assert.Equal("lua", task.ScriptFormat);
        Assert.Equal("return 1 < 2", task.Script);
    }

    [Fact]
    public void ClearingThemInTheModelClearsThemFromTheSourceToo()
    {
        var imported = BpmnXmlReader.Read(Xml("lua", "return 1"));
        var cleared = imported.Document with
        {
            FlowNodes = [.. imported.Document.FlowNodes.Select(node =>
                node.Type == BpmnFlowNodeType.ScriptTask ? node with { ScriptFormat = null, Script = null } : node)],
        };

        var written = BpmnXmlWriter.Write(cleared, BpmnWriteOptions.Fidelity);

        Assert.DoesNotContain("scriptFormat", written.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<bpmn:script>", written.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public void AScriptElementOnAnythingButAScriptTaskIsLeftAlone()
    {
        // Not valid BPMN, and not modelled, so a save must not be the thing that deletes it.
        var imported = BpmnXmlReader.Read($"""
            {Header}
                <bpmn:serviceTask id="Task_1">
                  <bpmn:script>keep me</bpmn:script>
                </bpmn:serviceTask>
            {Footer}
            """);

        var xml = BpmnXmlWriter.Write(imported.Document, BpmnWriteOptions.Fidelity).Xml;

        Assert.Contains("<bpmn:script>keep me</bpmn:script>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void AScriptIsWrittenAfterEverythingElseInTheTask()
    {
        // scriptTask's xsd:sequence puts script last; a document with it earlier is rejected by other tools.
        var imported = BpmnXmlReader.Read($"""
            {Header}
                <bpmn:scriptTask id="Task_1" scriptFormat="lua">
                  <bpmn:script>return 1</bpmn:script>
                  <bpmn:documentation>Notes</bpmn:documentation>
                </bpmn:scriptTask>
            {Footer}
            """);

        var xml = BpmnXmlWriter.Write(imported.Document).Xml;

        Assert.True(
            xml.IndexOf("<bpmn:documentation>", StringComparison.Ordinal) < xml.IndexOf("<bpmn:script>", StringComparison.Ordinal),
            xml);
    }

    // --- the core profile --------------------------------------------------------------

    [Fact]
    public void ATaskWithNoFormatIsReported()
    {
        var found = Run(Document(format: null, script: "return 1"), []);

        Assert.Equal("PROF-111", Assert.Single(found).RuleId);
    }

    [Fact]
    public void ATaskWithNoScriptIsReported()
    {
        var found = Run(Document("lua", "   "), []);

        Assert.Equal("PROF-113", Assert.Single(found).RuleId);
    }

    [Fact]
    public void SeveritiesAreTheHostsToSet()
    {
        var found = Run(
            Document(format: null, script: null),
            [],
            new BpmnScriptOptions { MissingFormatSeverity = BpmnSeverity.Error, EmptyScriptSeverity = BpmnSeverity.Info });

        Assert.Equal(BpmnSeverity.Error, Assert.Single(found, d => d.RuleId == "PROF-111").Severity);
        Assert.Equal(BpmnSeverity.Info, Assert.Single(found, d => d.RuleId == "PROF-113").Severity);
    }

    [Fact]
    public void AFormatTheEngineDoesNotRunIsAnError()
    {
        var found = Run(
            Document("python", "print(1)"),
            [],
            new BpmnScriptOptions { AllowedFormats = new HashSet<string> { "csharp", "JavaScript" } });

        Assert.Equal(BpmnSeverity.Error, Assert.Single(found, d => d.RuleId == "PROF-112").Severity);

        // Already rejected, so "no validator for it" would be the same finding a second time.
        Assert.DoesNotContain(found, d => d.RuleId == "PROF-115");
    }

    [Fact]
    public void TheAllowedListIgnoresCase()
    {
        var found = Run(
            Document("JAVASCRIPT", "return 1"),
            [],
            new BpmnScriptOptions { AllowedFormats = new HashSet<string> { "javascript" } });

        Assert.DoesNotContain(found, d => d.RuleId == "PROF-112");
    }

    [Fact]
    public void ATooLongScriptIsReportedAndNotParsed()
    {
        var validator = new Recording("lua");
        var found = Run(Document("lua", new string('x', 20)), [validator], new BpmnScriptOptions { MaxScriptLength = 10 });

        Assert.Equal("PROF-114", Assert.Single(found).RuleId);
        Assert.Equal(0, validator.Calls);
    }

    [Fact]
    public void AFormatWithNoValidatorIsNoted()
    {
        var found = Run(Document("groovy", "println 1"), []);

        var note = Assert.Single(found);
        Assert.Equal("PROF-115", note.RuleId);
        Assert.Equal(BpmnSeverity.Info, note.Severity);
    }

    [Fact]
    public void TheValidatorIsChosenByFormatIgnoringCase()
    {
        var lua = new Recording("lua");
        var js = new Recording("javascript");

        Run(Document("LuA", "return 1"), [js, lua]);

        Assert.Equal(1, lua.Calls);
        Assert.Equal(0, js.Calls);
    }

    [Fact]
    public void TheFirstRegisteredValidatorForAFormatWins()
    {
        var host = new Recording("lua");
        var package = new Recording("lua");

        Run(Document("lua", "return 1"), [host, package]);

        Assert.Equal(1, host.Calls);
        Assert.Equal(0, package.Calls);
    }

    [Fact]
    public void ASyntaxErrorCarriesItsLineAndColumn()
    {
        var found = Run(Document("lua", "local a = 1\nlocal b = 2"), [new Recording("lua", new BpmnScriptDiagnostic("boom", 16, 1))]);

        var finding = Assert.Single(found);
        Assert.Equal("PROF-116", finding.RuleId);
        Assert.Equal(BpmnSeverity.Error, finding.Severity);
        Assert.Contains("line 2, column 5: boom", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AValidatorThatThrowsDoesNotHideTheOtherTasks()
    {
        var document = Document("lua", "return 1");
        document = document with
        {
            FlowNodes = [.. document.FlowNodes, document.FlowNodes[0] with { Id = "Task_2", ScriptFormat = "js" }],
        };

        var found = Run(document, [new Exploding("lua"), new Recording("js", new BpmnScriptDiagnostic("late", 0, 0))]);

        Assert.Equal(BpmnSeverity.Warning, Assert.Single(found, d => d.RuleId == "PROF-117").Severity);
        Assert.Equal("Task_2", Assert.Single(found, d => d.RuleId == "PROF-116").ElementId);
    }

    [Fact]
    public void OnlyScriptTasksAreChecked()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.ServiceTask, new Bounds(0, 0, 100, 80), "Call")
            .Build();

        Assert.Empty(Run(document, []));
    }

    [Theory]
    [InlineData("", 0, 1, 1)]
    [InlineData("ab\ncd", 4, 2, 2)]
    [InlineData("ab\r\ncd", 5, 2, 2)]
    [InlineData("ab", 99, 1, 3)]
    [InlineData("ab", -5, 1, 1)]
    [InlineData("ab\rcd", 4, 2, 2)]
    [InlineData("ab\r\r\ncd", 6, 3, 2)]
    public void LocatingAnOffset(string source, int offset, int line, int column) =>
        Assert.Equal((line, column), BpmnScriptTaskProfile.Locate(source, offset));

    // --- the backends ------------------------------------------------------------------

    [Theory]
    [InlineData("var total = 1 + 2;\nreturn total;")]
    [InlineData("using System;\nConsole.WriteLine(\"hi\");")]
    [InlineData("int Add(int a, int b) => a + b;\nreturn Add(1, 2);")]
    public void ValidCSharpHasNoFindings(string source) =>
        Assert.Empty(new BpmnCSharpScriptValidator().Validate("csharp", source, Context));

    [Fact]
    public void BrokenCSharpIsLocated()
    {
        var found = Assert.Single(new BpmnCSharpScriptValidator().Validate("csharp", "var x = ;", Context));

        Assert.Equal(BpmnSeverity.Error, found.Severity);
        Assert.Equal(8, found.Offset);
        Assert.Contains("CS1525", found.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CSharpIsOnlyParsedNeverResolved()
    {
        // An undeclared name and a missing type are the engine's to judge, not the parser's.
        Assert.Empty(new BpmnCSharpScriptValidator().Validate("csharp", "return Undeclared.Missing(1);", Context));
    }

    [Theory]
    [InlineData("var x = 1;\nreturn x;")]
    [InlineData("function f(a) { return a * 2; }\nf(3);")]
    [InlineData("const o = { a: [1, 2, 3] };\nexecution.setVariable('o', o);")]
    public void ValidJavaScriptHasNoFindings(string source) =>
        Assert.Empty(new BpmnJavaScriptScriptValidator().Validate("javascript", source, Context));

    [Fact]
    public void BrokenJavaScriptIsLocated()
    {
        var found = Assert.Single(new BpmnJavaScriptScriptValidator().Validate("javascript", "var x = ;", Context));

        Assert.Equal(BpmnSeverity.Error, found.Severity);
        Assert.Equal(8, found.Offset);
    }

    [Fact]
    public void JavaScriptIsNeverRun()
    {
        // If this ran it would loop forever; parsing it is instant.
        Assert.Empty(new BpmnJavaScriptScriptValidator().Validate("javascript", "while (true) {}", Context));
    }

    [Theory]
    [InlineData("local x = 1\nreturn x + 2")]
    [InlineData("function f(a) return a * 2 end\nreturn f(3)")]
    [InlineData("for i = 1, 3 do print(i) end")]
    [InlineData("local x <const> = 5\nlocal y <close> = nil")]
    [InlineData("return 7 // 2 | 3 & 1 ~ 2 << 1, ~0")]
    [InlineData("goto done\n::done::\nreturn 0x1p4")]
    public void ValidLuaHasNoFindings(string source) =>
        Assert.Empty(new BpmnLuaScriptValidator().Validate("lua", source, Context));

    [Fact]
    public void BrokenLuaIsLocated()
    {
        const string Source = "local a = 1\nlocal b = = 2";
        var found = new BpmnLuaScriptValidator().Validate("lua", Source, Context);

        // Offsets are 0-based, and the first finding is on the second '=' of the second line.
        var first = found[0];
        Assert.Equal(22, first.Offset);
        Assert.Equal('=', Source[first.Offset]);
        Assert.All(found, d => Assert.Equal(BpmnSeverity.Error, d.Severity));
    }

    [Fact]
    public void EveryLuaMistakeIsReportedInSourceOrder()
    {
        var found = new BpmnLuaScriptValidator().Validate("lua", "local a = = 1\nlocal b = = 2\nx = (", Context);

        Assert.True(found.Count >= 3, "each mistake should be reported, not just the first");
        Assert.Equal(found.Select(d => d.Offset).Order(), found.Select(d => d.Offset));
        Assert.Contains(found, d => d.Offset >= 14);
    }

    [Theory]
    [InlineData("x = 7 // 2")]
    [InlineData("x = 1 | 2")]
    public void TheLuaDialectIsTheHostsToChoose(string source)
    {
        Assert.Empty(new BpmnLuaScriptValidator().Validate("lua", source, Context));
        Assert.NotEmpty(new BpmnLuaScriptValidator(Loretta.CodeAnalysis.Lua.LuaSyntaxOptions.Lua51).Validate("lua", source, Context));
    }

    [Fact]
    public void AnUnterminatedLuaStringIsLocated()
    {
        var found = new BpmnLuaScriptValidator().Validate("lua", "x = 'abc\ny = 1", Context);

        // Reported where the string starts, not at the line break that ends it.
        Assert.Equal(4, found[0].Offset);
    }

    [Fact]
    public void LuaIsNeverRun()
    {
        Assert.Empty(new BpmnLuaScriptValidator().Validate("lua", "while true do end", Context));
    }

    [Fact]
    public void EveryBackendOwnsItsOwnFormats()
    {
        Assert.Contains("c#", new BpmnCSharpScriptValidator().Formats);
        Assert.Contains("JS", new BpmnJavaScriptScriptValidator().Formats);
        Assert.Contains("LUA", new BpmnLuaScriptValidator().Formats);
    }

    [Theory]
    [InlineData("csharp", "var x = ")]
    [InlineData("javascript", "var x = ")]
    [InlineData("lua", "x = ")]
    public void DeepBracketNestingIsRefusedBeforeAnyParserSeesIt(string format, string prefix)
    {
        /*
         * Ten thousand open parentheses overflow Roslyn's parser and three thousand overflow
         * a Lua parser's, and a stack overflow takes the host down with it. If the check regresses this
         * test does not fail, it kills the test run, which is the point: the hazard is a dead
         * process, not a wrong answer.
         */
        var found = Run(Document(format, prefix + new string('(', 60_000)), AllBackends);

        Assert.Equal("PROF-118", Assert.Single(found).RuleId);
    }

    [Theory]
    [InlineData("csharp", "var x = ", "!", "true;")]
    [InlineData("javascript", "var x = ", "!", "true;")]
    [InlineData("lua", "x = ", "not ", "1")]
    public void ADeepChainWithNoBracketsStillCannotEndTheProcess(string format, string prefix, string operatorText, string suffix)
    {
        // A bracket count cannot see this nesting; the parser's own large stack is what survives it.
        var source = prefix + string.Concat(Enumerable.Repeat(operatorText, 15_000)) + suffix;

        var found = Run(Document(format, source), AllBackends);

        Assert.DoesNotContain(found, d => d.RuleId == "PROF-117");
    }

    [Theory]
    [InlineData("", 100, 0)]
    [InlineData("f(a[0], {b: (c)})", 100, 3)]
    [InlineData("))) (", 100, 1)]
    [InlineData("((((", 2, 3)]
    [InlineData("x = \"((((((((\" .. 'y'", 100, 8)]
    [InlineData("x = \"))))\"; f((1))", 100, 2)]
    [InlineData("-- don't (((\nf(1)", 100, 4)]
    public void CountingNesting(string source, int limit, int expected) =>
        Assert.Equal(expected, BpmnScriptTaskProfile.NestingDepth(source, limit));

    [Fact]
    public void AStrayQuoteCannotHideNesting()
    {
        // Skipping "strings" alone would see none of this: the quote swallows the rest of the line.
        var source = "x = ' " + new string('(', 150);

        Assert.Equal("PROF-118", Assert.Single(Run(Document("lua", source), AllBackends)).RuleId);
    }

    [Fact]
    public void AStringOfClosersCannotOffsetNesting()
    {
        // Counting every bracket alone would see depth 1 throughout: each ")" in a string cancels a "(".
        var source = "x = " + string.Concat(Enumerable.Repeat("( \")\" ", 150)) + "1";

        Assert.Equal("PROF-118", Assert.Single(Run(Document("lua", source), AllBackends)).RuleId);
    }

    [Fact]
    public void TheSeverityOfAFailedValidatorIsTheHostsToSet()
    {
        var found = Run(
            Document("lua", "return 1"),
            [new Exploding("lua")],
            new BpmnScriptOptions { ValidatorFailureSeverity = BpmnSeverity.Error });

        Assert.Equal(BpmnSeverity.Error, Assert.Single(found, d => d.RuleId == "PROF-117").Severity);
    }

    [Fact]
    public void TheNestingLimitIsTheHostsToSet()
    {
        var source = "return " + new string('(', 150) + "1" + new string(')', 150);

        Assert.Equal("PROF-118", Assert.Single(Run(Document("lua", source), AllBackends)).RuleId);
        Assert.DoesNotContain(
            Run(Document("lua", source), AllBackends, new BpmnScriptOptions { MaxNestingDepth = 0 }),
            d => d.RuleId == "PROF-118");
    }

    [Fact]
    public void SwitchingATaskAwayFromScriptTaskDropsTheScript()
    {
        var document = BpmnXmlReader.Read(Xml("lua", "return 1")).Document;

        var toService = BpmnDocumentEditor.Apply(document, new SwitchElementTypeCommand("Task_1", BpmnFlowNodeType.ServiceTask));

        Assert.True(toService.IsApplied);
        var node = toService.Document.FindFlowNode("Task_1")!;
        Assert.Null(node.ScriptFormat);
        Assert.Null(node.Script);
        Assert.Contains(toService.Diagnostics, d => d.Code == "EDIT-082" && d.Message.Contains("script", StringComparison.Ordinal));
    }

    // --- end to end --------------------------------------------------------------------

    [Fact]
    public void RegisteredBackendsValidateARealDocumentThroughDependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddBpmnComponents().AddBpmnCSharpScripting().AddBpmnJavaScriptScripting().AddBpmnLuaScripting();
        using var provider = services.BuildServiceProvider();

        var document = BpmnXmlReader.Read($"""
            {Header}
                <bpmn:scriptTask id="Csharp" scriptFormat="csharp"><bpmn:script>var x = ;</bpmn:script></bpmn:scriptTask>
                <bpmn:scriptTask id="Js" scriptFormat="javascript"><bpmn:script>return 1;</bpmn:script></bpmn:scriptTask>
                <bpmn:scriptTask id="Lua" scriptFormat="lua"><bpmn:script>local = 1</bpmn:script></bpmn:scriptTask>
            {Footer}
            """).Document;

        var report = BpmnValidator.Validate(document, [.. provider.GetServices<IBpmnProfileValidator>()]);
        var script = report.Diagnostics.Where(d => d.RuleId == "PROF-116").ToList();

        Assert.Equal(["Csharp", "Lua"], script.Select(d => d.ElementId).Order().ToArray());
    }

    [Fact]
    public void TheLastLuaDialectAskedForWinsWhateverTheOrder()
    {
        var services = new ServiceCollection()
            .AddBpmnLuaScripting()
            .AddBpmnLuaScripting(Loretta.CodeAnalysis.Lua.LuaSyntaxOptions.Lua51)
            .AddBpmnLuaScripting()
            .AddBpmnLuaScripting(Loretta.CodeAnalysis.Lua.LuaSyntaxOptions.Lua51);
        using var provider = services.BuildServiceProvider();

        var lua = Assert.Single(provider.GetServices<IBpmnScriptValidator>());

        Assert.NotEmpty(lua.Validate("lua", "x = 7 // 2", Context));
    }

    // --- fixtures ----------------------------------------------------------------------

    private static readonly IBpmnScriptValidator[] AllBackends =
        [new BpmnCSharpScriptValidator(), new BpmnJavaScriptScriptValidator(), new BpmnLuaScriptValidator()];

    private static readonly BpmnScriptContext Context = new("Task_1", "Work");

    private static IReadOnlyList<BpmnProfileDiagnostic> Run(
        BpmnDocument document,
        IEnumerable<IBpmnScriptValidator> validators,
        BpmnScriptOptions? options = null) =>
        [.. new BpmnScriptTaskProfile(validators, Options.Create(options ?? new BpmnScriptOptions())).Validate(document)];

    private static BpmnDocument Document(string? format, string? script)
    {
        var built = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.ScriptTask, new Bounds(0, 0, 100, 80), "Work")
            .Build();

        return built with
        {
            FlowNodes = [.. built.FlowNodes.Select(node => node with { ScriptFormat = format, Script = script })],
        };
    }

    private static string Xml(string format, string script) => $"""
        {Header}
            <bpmn:scriptTask id="Task_1" name="Work" scriptFormat="{format}">
              <bpmn:script>{System.Security.SecurityElement.Escape(script)}</bpmn:script>
            </bpmn:scriptTask>
        {Footer}
        """;

    private static BpmnFlowNode ReadTask(string format, string script) =>
        Task(BpmnXmlReader.Read(Xml(format, script)).Document);

    private static BpmnFlowNode Task(BpmnDocument document) =>
        Assert.Single(document.FlowNodes, node => node.Type == BpmnFlowNodeType.ScriptTask);

    private sealed class Recording(string format, params BpmnScriptDiagnostic[] findings) : IBpmnScriptValidator
    {
        public int Calls { get; private set; }

        public IReadOnlySet<string> Formats { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { format };

        public IReadOnlyList<BpmnScriptDiagnostic> Validate(string format, string source, BpmnScriptContext context)
        {
            Calls++;
            return findings;
        }
    }

    private sealed class Exploding(string format) : IBpmnScriptValidator
    {
        public IReadOnlySet<string> Formats { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { format };

        public IReadOnlyList<BpmnScriptDiagnostic> Validate(string format, string source, BpmnScriptContext context) =>
            throw new InvalidOperationException("parser crashed");
    }
}
