using HttpClientCache;
using HttpClientCache.Files;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class CacheExtensions
{
    public static IHttpClientBuilder AddResponseCache(this IHttpClientBuilder builder)
    {
        AddCoreServices(builder.Services);
        return builder.AddHttpMessageHandler(provider =>
        {
            var cache = provider.GetService<IHttpCache>() ?? FileCache.Default;
            return ActivatorUtilities.CreateInstance<CacheHandler>(provider, cache);
        });
    }

    public static IHttpClientBuilder AddResponseCache(
        this IHttpClientBuilder builder,
        IHttpCache cache
    )
    {
        AddCoreServices(builder.Services);
        return builder.AddHttpMessageHandler(provider =>
            ActivatorUtilities.CreateInstance<CacheHandler>(provider, cache)
        );
    }

    private static void AddCoreServices(IServiceCollection services)
    {
        services.TryAddSingleton<ICacheKeyComputer, CacheKeyComputer>();
    }
}
