using RayanMockServer.Models;

namespace RayanMockServer.Services;

public interface IMockCacheService
{
    Task<IReadOnlyList<MockApiEndpoint>> GetActiveEndpointsAsync();
    IReadOnlyList<MockApiEndpoint> GetActiveEndpointsSync();
    Task<IReadOnlyDictionary<string, string>> GetGlobalVariablesAsync();
    void InvalidateCache();
}
