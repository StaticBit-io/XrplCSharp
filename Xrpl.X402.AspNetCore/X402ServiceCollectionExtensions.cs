using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xrpl.Client;

namespace Xrpl.X402.AspNetCore;

/// <summary>Registers the x402 facilitator services in an <see cref="IServiceCollection"/>.</summary>
public static class X402ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="LedgerSettlingFacilitator"/> as the singleton <see cref="IX402Facilitator"/>,
    /// settling through the <see cref="IXrplClient"/> already registered, with an
    /// <see cref="InMemorySettledPaymentStore"/> unless an <see cref="ISettledPaymentStore"/> is
    /// registered first.
    /// </summary>
    /// <remarks>
    /// Register a shared <see cref="ISettledPaymentStore"/> before calling this when several instances
    /// settle for the same merchant: the in-memory default sees only its own process.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddLedgerSettlingFacilitator(this IServiceCollection services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<ISettledPaymentStore>(_ => new InMemorySettledPaymentStore());
        services.TryAddSingleton(provider => new LedgerSettlingFacilitator(
            provider.GetRequiredService<IXrplClient>(),
            provider.GetRequiredService<ISettledPaymentStore>()));
        services.TryAddSingleton<IX402Facilitator>(provider => provider.GetRequiredService<LedgerSettlingFacilitator>());
        return services;
    }
}
