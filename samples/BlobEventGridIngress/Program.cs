using BlobEventGridIngress;

using Hasseware.AspNetCore.Components.Bpmn.Storage;

// Forwards Azure Event Grid blob events to the change bus, so that editors hear about documents written by anything that bypasses the store.
// Setup (see README.md): an Event Grid subscription on the storage account for BlobCreated and BlobDeleted, filtered to the container,
// with this endpoint as its webhook and the shared key in the delivery's aeg-sas-key header.
var builder = WebApplication.CreateBuilder(args);

var options = new BlobIngressOptions(
    Container: builder.Configuration["Ingress:Container"] ?? throw new InvalidOperationException("Set Ingress:Container."),
    Prefix: builder.Configuration["Ingress:Prefix"],
    SharedSecret: builder.Configuration["Ingress:SharedSecret"] ?? throw new InvalidOperationException("Set Ingress:SharedSecret."));

// The host's change bus, the one its editors' transports listen to; with more than one instance this is a bus that spans instances.
builder.Services.AddBpmnDocumentChanges();
builder.Services.AddSingleton(provider => new BpmnDocumentChangeIngress(provider.GetRequiredService<IBpmnDocumentChangeBus>()));

var app = builder.Build();
app.MapBlobEventGridIngress(options);
app.Run();
