using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

internal static class CompletionCorpus
{
    public static void Report()
    {
        BpmnFeelVariable[] variables =
        [
            new("amount", "first"),
            new("amount", "duplicate"),
            new("customer") { Entries = [new("full name"), new("address") { Entries = [new("city")] }] },
            new("sum", "variable shadows function"),
            new("customer.address.city", "duplicate path"),
        ];
        string[] sources = ["", "am", "sum(", "customer.", "customer.full name + 1", "string j",
            "😀 + amouZZ + 1", "\"customer", "// amount", "for invoice amount in [1] return invoice a",
            "{ first: 1, second: fir", "missing"];
        var cases = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var source in sources)
        {
            var tokens = FeelSharpLanguage.Tokenize(source);
            foreach (var ranked in new[] { false, true })
            {
                var options = new BpmnFeelSharpEditorOptions
                {
                    LocalVariables = true,
                    Snippets = true,
                    RankCompletions = ranked,
                    Functions = [new("sum", "custom sum(value)", "custom")],
                };
                for (var start = 0; start <= source.Length; start++)
                {
                    foreach (var end in new[] { start, source.Length })
                    {
                        foreach (var explicitRequest in new[] { false, true })
                        {
                            var completions = FeelSharpLanguage.Complete(source, start, end, variables,
                                explicitRequest, options, tokens);
                            hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(completions)));
                            cases++;
                        }
                    }
                }
            }
        }
        Console.WriteLine($"completion corpus: {cases} cases, SHA256={Convert.ToHexString(hash.GetHashAndReset())}");
    }
}
