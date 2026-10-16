using Hasseware.AspNetCore.Components.Bpmn.Configuration;

using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>Creates <see cref="BpmnDocumentBinding"/>s over the registered store.</summary>
/// <remarks>
/// One binding per editor: a page with two editors creates two. The host owns each binding it creates
/// and disposes it.
/// </remarks>
public interface IBpmnDocumentBindingFactory
{
    /// <summary>Creates a binding with nothing loaded.</summary>
    /// <returns>A new binding.</returns>
    public BpmnDocumentBinding Create();
}

/// <summary>The default factory, using the registered store, change subscriber, options and clock.</summary>
/// <param name="store">The registered store.</param>
/// <param name="subscriber">The registered change subscriber, when there is one.</param>
/// <param name="options">The editor options, when registered.</param>
/// <param name="timeProvider">The registered clock, when there is one.</param>
internal sealed class BpmnDocumentBindingFactory(
    IBpmnDocumentStore store,
    IBpmnDocumentChangeSubscriber? subscriber,
    IOptions<BpmnOptions>? options,
    TimeProvider? timeProvider) : IBpmnDocumentBindingFactory
{
    public BpmnDocumentBinding Create() => new(store, subscriber, options?.Value.Storage, timeProvider) { DeferUntilAttached = true };
}
