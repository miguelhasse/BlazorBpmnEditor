using Hasseware.AspNetCore.Components.Bpmn.Mcp.Tool;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    return await BpmnMcpCli.RunAsync(args, Console.Out, Console.Error, cancellation.Token).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    return BpmnMcpCli.Usage;
}
