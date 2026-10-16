using Hasseware.AspNetCore.Components.Bpmn.Storage;

using PostgresNotifyIngress;

// Forwards PostgreSQL LISTEN/NOTIFY signals about the documents table to the change bus, so that editors hear about rows written by anything
// that bypasses the store. Create the trigger in trigger.sql first (see README.md).
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Documents") ?? throw new InvalidOperationException("Set ConnectionStrings:Documents.");

// The host's change bus, the one its editors' transports listen to; with more than one instance this is a bus that spans instances.
builder.Services.AddBpmnDocumentChanges();
builder.Services.AddSingleton(provider => new BpmnDocumentChangeIngress(provider.GetRequiredService<IBpmnDocumentChangeBus>()));
builder.Services.AddHostedService(provider => new PostgresNotifyIngressService(
    connectionString,
    provider.GetRequiredService<BpmnDocumentChangeIngress>(),
    provider.GetRequiredService<ILogger<PostgresNotifyIngressService>>()));

await builder.Build().RunAsync();
