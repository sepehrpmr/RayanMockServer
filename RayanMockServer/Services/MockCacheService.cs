using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RayanMockServer.Data;
using RayanMockServer.Models;

namespace RayanMockServer.Services;

public class MockCacheService : IMockCacheService
{
    private readonly IMemoryCache _cache;
    private readonly IServiceProvider _serviceProvider;
    private const string ActiveEndpointsCacheKey = "ActiveMockEndpoints";
    private const string GlobalVariablesCacheKey = "GlobalVariablesCache";
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromHours(12);

    public MockCacheService(IMemoryCache cache, IServiceProvider serviceProvider)
    {
        _cache = cache;
        _serviceProvider = serviceProvider;
    }

    public async Task<IReadOnlyList<MockApiEndpoint>> GetActiveEndpointsAsync()
    {
        if (_cache.TryGetValue(ActiveEndpointsCacheKey, out IReadOnlyList<MockApiEndpoint>? cached) && cached != null)
        {
            return cached;
        }

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var endpoints = await context.MockApiEndpoints
            .AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.IndexNumber)
            .ThenBy(e => e.Id)
            .ToListAsync();

        _cache.Set(ActiveEndpointsCacheKey, (IReadOnlyList<MockApiEndpoint>)endpoints, DefaultCacheDuration);
        return endpoints;
    }

    public IReadOnlyList<MockApiEndpoint> GetActiveEndpointsSync()
    {
        if (_cache.TryGetValue(ActiveEndpointsCacheKey, out IReadOnlyList<MockApiEndpoint>? cached) && cached != null)
        {
            return cached;
        }

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var endpoints = context.MockApiEndpoints
            .AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.IndexNumber)
            .ThenBy(e => e.Id)
            .ToList();

        _cache.Set(ActiveEndpointsCacheKey, (IReadOnlyList<MockApiEndpoint>)endpoints, DefaultCacheDuration);
        return endpoints;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetGlobalVariablesAsync()
    {
        if (_cache.TryGetValue(GlobalVariablesCacheKey, out IReadOnlyDictionary<string, string>? cached) && cached != null)
        {
            return cached;
        }

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var variables = await context.GlobalVariables
            .AsNoTracking()
            .ToDictionaryAsync(v => v.Key, v => v.Value);

        _cache.Set(GlobalVariablesCacheKey, (IReadOnlyDictionary<string, string>)variables, DefaultCacheDuration);
        return variables;
    }

    public void InvalidateCache()
    {
        _cache.Remove(ActiveEndpointsCacheKey);
        _cache.Remove(GlobalVariablesCacheKey);
    }
}
