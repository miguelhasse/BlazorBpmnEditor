using System.Text.Json;

using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests.Support;

/// <summary>
/// A browser as the component sees it: it imports the module, says whether WebMCP is there, registers the
/// tools it is given and lets a test call them, the way an agent would.
/// </summary>
internal sealed class FakeBrowser : IJSRuntime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public FakeBrowser(bool supported = true)
    {
        Module = new FakeModule { Supported = supported };
    }

    public FakeModule Module { get; }

    public bool ImportFails { get; init; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        if (identifier != "import") throw new InvalidOperationException($"Unexpected runtime call '{identifier}'.");
        if (ImportFails) return ValueTask.FromException<TValue>(new JSException("Module unavailable"));

        return new ValueTask<TValue>((TValue)(object)Module);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);

    internal sealed class FakeModule : IJSObjectReference
    {
        public bool Supported { get; set; }

        /// <summary>Names the fake browser refuses, as the real one refuses a duplicate.</summary>
        public HashSet<string> Refused { get; } = [];

        public List<BpmnWebMcpToolDescriptor> Registered { get; } = [];

        public List<(string Name, string Message)> Failures { get; } = [];

        /// <summary>When set, registration does not answer until the test completes it, as a slow browser does.</summary>
        public TaskCompletionSource? RegisterGate { get; set; }

        public int Unregistrations { get; private set; }

        public int Disposals { get; private set; }

        public DotNetObjectReference<BpmnWebMcpBridge>? Reference { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            switch (identifier)
            {
                case "isSupported":
                    return new ValueTask<TValue>((TValue)(object)Supported);

                case "registerAll":
                    Reference = (DotNetObjectReference<BpmnWebMcpBridge>)args![0]!;
                    foreach (var tool in (IEnumerable<BpmnWebMcpToolDescriptor>)args[1]!)
                    {
                        if (Refused.Contains(tool.Name)) Failures.Add((tool.Name, "A tool with that name is already registered."));
                        else Registered.Add(tool);
                    }

                    var json = JsonSerializer.Serialize(
                        new
                        {
                            id = 1,
                            registered = Registered.Select(tool => tool.Name),
                            failures = Failures.Select(failure => new { name = failure.Name, message = failure.Message }),
                        },
                        Web);
                    var answer = JsonSerializer.Deserialize<TValue>(json, Web)!;
                    return RegisterGate is { } gate ? new ValueTask<TValue>(Gated(gate, answer)) : new ValueTask<TValue>(answer);

                case "unregisterAll":
                    Unregistrations++;
                    Registered.Clear();
                    return new ValueTask<TValue>(default(TValue)!);

                default:
                    throw new InvalidOperationException($"Unexpected module call '{identifier}'.");
            }
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        private static async Task<TValue> Gated<TValue>(TaskCompletionSource gate, TValue answer)
        {
            await gate.Task;
            return answer;
        }

        /// <summary>Calls a registered tool the way the browser would for an agent, and parses what comes back.</summary>
        public async Task<JsonElement> CallAsync(string tool, string argumentsJson = "{}")
        {
            var json = await Reference!.Value.ExecuteAsync(Guid.NewGuid().ToString("N"), tool, argumentsJson);
            return JsonElement.Parse(json);
        }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
