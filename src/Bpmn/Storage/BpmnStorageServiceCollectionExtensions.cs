using Hasseware.AspNetCore.Components.Bpmn.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>Registers a document store, change notifications and document bindings.</summary>
/// <remarks>
/// <para>
/// Nothing here is registered by <see cref="BpmnServiceCollectionExtensions.AddBpmnComponents"/>: a host
/// that never persists documents pays nothing. Register a store, and optionally change notifications:
/// </para>
/// <code>
/// builder.Services.AddBpmnEditor();
/// builder.Services.AddBpmnDocumentStore&lt;MyStore&gt;();
/// builder.Services.AddBpmnDocumentChanges();
/// </code>
/// <para>
/// When a change bus is registered, the store is wrapped in a <see cref="NotifyingBpmnDocumentStore"/>.
/// That is decided when the store is resolved, not when it is registered, so the order of these calls
/// does not matter, and the wrapper has the store's own lifetime.
/// </para>
/// </remarks>
public static class BpmnStorageServiceCollectionExtensions
{
    /// <summary>Registers the document store every editor, tool and host uses.</summary>
    /// <typeparam name="TStore">The store; constructed by the container.</typeparam>
    /// <param name="services">Host services.</param>
    /// <param name="lifetime">
    /// The store's lifetime: singleton for a stateless store, scoped for one that holds a per-request
    /// resource such as a database context.
    /// </param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddBpmnDocumentStore<TStore>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TStore : class, IBpmnDocumentStore
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAdd(new ServiceDescriptor(typeof(TStore), typeof(TStore), lifetime));
        return services.AddBpmnDocumentStore(provider => provider.GetRequiredService<TStore>(), lifetime);
    }

    /// <summary>Registers the document store every editor, tool and host uses, built by a factory.</summary>
    /// <param name="services">Host services.</param>
    /// <param name="factory">Builds the store.</param>
    /// <param name="lifetime">The store's lifetime.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks>A later registration replaces an earlier one.</remarks>
    public static IServiceCollection AddBpmnDocumentStore(
        this IServiceCollection services,
        Func<IServiceProvider, IBpmnDocumentStore> factory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        services.Replace(new ServiceDescriptor(
            typeof(IBpmnDocumentStore),
            provider => Notifying(provider, factory(provider)),
            lifetime));

        services.TryAdd(new ServiceDescriptor(typeof(IBpmnDocumentBindingFactory), CreateBindingFactory, ServiceLifetime.Scoped));
        return services;
    }

    /// <summary>Registers a store held in memory, for tests, demos and the playground.</summary>
    /// <param name="services">Host services.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddBpmnInMemoryDocumentStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(provider => new InMemoryBpmnDocumentStore(provider.GetService<TimeProvider>()));
        return services.AddBpmnDocumentStore(provider => provider.GetRequiredService<InMemoryBpmnDocumentStore>());
    }

    /// <summary>
    /// Registers in-process change notifications, so editors hear about writes made through the registered
    /// store by any other party in this process.
    /// </summary>
    /// <param name="services">Host services.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks>
    /// Enough for Blazor Server on one instance. A host with several instances replaces
    /// <see cref="IBpmnDocumentChangeBus"/> with a broker-backed bus, and a WebAssembly client replaces
    /// <see cref="IBpmnDocumentChangeSubscriber"/> with a wire transport.
    /// </remarks>
    public static IServiceCollection AddBpmnDocumentChanges(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IBpmnDocumentChangeBus, InProcessBpmnDocumentChangeBus>();
        services.TryAddSingleton<IBpmnDocumentChangeSubscriber, BusBpmnDocumentChangeSubscriber>();
        return services;
    }

    private static IBpmnDocumentStore Notifying(IServiceProvider provider, IBpmnDocumentStore store) =>
        provider.GetService<IBpmnDocumentChangeBus>() is { } bus && store is not NotifyingBpmnDocumentStore
            ? new NotifyingBpmnDocumentStore(store, bus, provider.GetService<TimeProvider>())
            : store;

    private static BpmnDocumentBindingFactory CreateBindingFactory(IServiceProvider provider) => new(
        provider.GetRequiredService<IBpmnDocumentStore>(),
        provider.GetService<IBpmnDocumentChangeSubscriber>(),
        provider.GetService<IOptions<BpmnOptions>>(),
        provider.GetService<TimeProvider>());
}
