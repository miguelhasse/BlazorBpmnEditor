using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Resources;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Localization;

/// <summary>
/// Every Script Task rule resolves to a message, and each shipped language translates it without
/// losing a placeholder.
/// </summary>
public sealed partial class ScriptTaskLocalizationTests
{
    private static readonly ResourceManager Resources = new(typeof(BpmnRuleMessages));

    private static readonly string[] Keys = ["PROF-111", "PROF-112", "PROF-113", "PROF-114", "PROF-115", "PROF-116", "PROF-117", "PROF-118"];

    public static TheoryData<string> Languages =>
        ["cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant"];

    [Fact]
    public void EveryRuleTheProfileEmitsHasAnEnglishResource()
    {
        var emitted = Emit();

        // The key list above is the contract this class checks, so it must be the profile's own.
        Assert.Equal(Keys, emitted.Select(d => d.RuleId).Distinct().Order().ToArray());

        using var provider = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var localizer = provider.GetRequiredService<BpmnDiagnosticLocalizer>();

        foreach (var diagnostic in emitted)
        {
            Assert.NotNull(Resources.GetString(diagnostic.MessageResourceKey ?? diagnostic.RuleId, CultureInfo.InvariantCulture));
            Assert.Equal(diagnostic.Message, localizer.Localize(diagnostic));
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLanguageTranslatesEveryRuleAndKeepsItsPlaceholders(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);

        foreach (var key in Keys)
        {
            var english = Resources.GetString(key, CultureInfo.InvariantCulture)!;
            var translated = Resources.GetString(key, culture);

            Assert.NotNull(translated);
            Assert.NotEqual(english, translated);
            Assert.Equal(Placeholders(english), Placeholders(translated));
        }
    }

    [Fact]
    public void ASyntaxErrorIsLocalizedAroundTheParsersOwnWording()
    {
        var diagnostic = Assert.Single(Emit(), d => d.RuleId == "PROF-116");
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");

            using var provider = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
            var message = provider.GetRequiredService<BpmnDiagnosticLocalizer>().Localize(diagnostic);

            Assert.Contains("Zeile 2, Spalte 5", message, StringComparison.Ordinal);
            Assert.Contains("boom", message, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>One document and option set that makes the profile say every one of its rules.</summary>
    private static List<BpmnDiagnostic> Emit()
    {
        var options = new BpmnScriptOptions
        {
            AllowedFormats = new HashSet<string> { "lua", "lua2", "boom", "crash" },
            MaxScriptLength = 40,
            MaxNestingDepth = 3,
        };

        var validators = new IBpmnScriptValidator[] { new Scripted("lua", "boom"), new Scripted("boom", "boom"), new Crashing() };
        var profile = new BpmnScriptTaskProfile(validators, Options.Create(options));

        var builder = new BpmnDocumentBuilder();
        var tasks = new (string Id, string? Format, string? Script)[]
        {
            ("NoFormat", null, "x"),
            ("NotAllowed", "python", "x"),
            ("NoScript", "lua", null),
            ("TooLong", "lua", new string('x', 60)),
            ("NoValidator", "lua2", "x"),
            ("Syntax", "boom", "local a = 1\nlocal b = ?"),
            ("Crash", "crash", "x"),
            ("Nested", "lua", "((((x))))"),
        };

        foreach (var (id, _, _) in tasks)
        {
            builder.Node(id, BpmnFlowNodeType.ScriptTask, new Bounds(0, 0, 100, 80), id);
        }

        var built = builder.Build();
        var document = built with
        {
            FlowNodes = [.. built.FlowNodes.Select(node =>
            {
                var (Id, Format, Script) = tasks.Single(t => t.Id == node.Id);
                return node with { ScriptFormat = Format, Script = Script };
            })],
        };

        return [.. BpmnProfileRunner.Run(document, [profile])];
    }

    private static string[] Placeholders(string text) =>
        [.. Placeholder().Matches(text).Select(match => match.Value).Order()];

    [GeneratedRegex(@"\{\d\}")]
    private static partial Regex Placeholder();

    private sealed class Scripted(string format, string message) : IBpmnScriptValidator
    {
        public IReadOnlySet<string> Formats { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { format };

        // Offset 16 is the fifth character of the second line of the script used above.
        public IReadOnlyList<BpmnScriptDiagnostic> Validate(string format, string source, BpmnScriptContext context) =>
            source.Contains('?', StringComparison.Ordinal) ? [new BpmnScriptDiagnostic(message, 16, 1)] : [];
    }

    private sealed class Crashing : IBpmnScriptValidator
    {
        public IReadOnlySet<string> Formats { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crash" };

        public IReadOnlyList<BpmnScriptDiagnostic> Validate(string format, string source, BpmnScriptContext context) =>
            throw new InvalidOperationException("parser crashed");
    }
}
