using Hasseware.AspNetCore.Components.Bpmn.Storage;

using SqlServerChangeIngress;

// Forwards SQL Server changes to the documents table to the change bus, so that editors hear about rows written by anything that bypasses the
// store. Ingress:Mode chooses Service Broker (push; run service-broker.sql) or Change Tracking (pull; run change-tracking.sql). See README.md.
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Documents") ?? throw new InvalidOperationException("Set ConnectionStrings:Documents.");
var options = builder.Configuration.GetSection(SqlServerIngressOptions.Section).Get<SqlServerIngressOptions>() ?? new SqlServerIngressOptions();

// The host's change bus, the one its editors' transports listen to; with more than one instance this is a bus that spans instances.
builder.Services.AddBpmnDocumentChanges();
builder.Services.AddSingleton(provider => new BpmnDocumentChangeIngress(provider.GetRequiredService<IBpmnDocumentChangeBus>()));

switch (options.Mode)
{
    case SqlServerIngressMode.ServiceBroker:
        builder.Services.AddHostedService(provider => new ServiceBrokerIngressService(
            connectionString,
            options,
            provider.GetRequiredService<BpmnDocumentChangeIngress>(),
            provider.GetRequiredService<ILogger<ServiceBrokerIngressService>>()));
        break;

    case SqlServerIngressMode.ChangeTracking:
        builder.Services.AddHostedService(provider => new ChangeTrackingIngressService(
            connectionString,
            options,
            provider.GetRequiredService<BpmnDocumentChangeIngress>(),
            provider.GetRequiredService<ILogger<ChangeTrackingIngressService>>()));
        break;

    default:
        throw new InvalidOperationException($"Ingress:Mode must be {nameof(SqlServerIngressMode.ServiceBroker)} or {nameof(SqlServerIngressMode.ChangeTracking)}.");
}

await builder.Build().RunAsync();
