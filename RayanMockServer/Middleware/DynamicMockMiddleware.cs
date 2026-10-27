using System.Text;
using System.Text.Json;
using RayanMockServer.Models;
using RayanMockServer.Services;

namespace RayanMockServer.Middleware;

public class DynamicMockMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DynamicMockMiddleware> _logger;

    public DynamicMockMiddleware(RequestDelegate next, ILogger<DynamicMockMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IMockCacheService cacheService)
    {
        var path = context.Request.Path.Value ?? "/";

        // Ignore swagger, admin web UI, and management API calls
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/admin-mgmt/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Apply standard CORS headers required by Rayan client apps
        ApplyCorsHeaders(context);

        string reqMethod = context.Request.Method.ToUpperInvariant();
        string reqPath = path.TrimEnd('/');
        if (string.IsNullOrEmpty(reqPath)) reqPath = "/";

        var activeEndpoints = await cacheService.GetActiveEndpointsAsync();

        // 1. Search for matching endpoint: Exact match has priority over StartsWith
        MockApiEndpoint? matchedEndpoint = activeEndpoints
            .FirstOrDefault(e => e.HttpMethod.Equals(reqMethod, StringComparison.OrdinalIgnoreCase) &&
                                 e.UrlPath.Equals(reqPath, StringComparison.OrdinalIgnoreCase));

        // 2. If no exact match, try prefix match for parametric paths (e.g., /api/v1/mobile/pdfs/omidnameh.pdf)
        if (matchedEndpoint == null)
        {
            matchedEndpoint = activeEndpoints
                .Where(e => e.HttpMethod.Equals(reqMethod, StringComparison.OrdinalIgnoreCase) &&
                            reqPath.StartsWith(e.UrlPath, StringComparison.OrdinalIgnoreCase) &&
                            e.UrlPath.Length > 1)
                .OrderByDescending(e => e.UrlPath.Length) // Pick the most specific prefix match
                .FirstOrDefault();
        }

        // 3. Handle OPTIONS requests (CORS preflight or explicit OPTIONS endpoint)
        if (reqMethod == "OPTIONS")
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = 200;

            if (matchedEndpoint != null && !string.IsNullOrWhiteSpace(matchedEndpoint.SuccessResponseJson))
            {
                await context.Response.WriteAsync(matchedEndpoint.SuccessResponseJson, Encoding.UTF8);
            }
            else
            {
                // Standard preflight filter response matching Node.js mock server
                string defaultOptionsJson = JsonSerializer.Serialize(new
                {
                    filters = new[]
                    {
                        new { label = "کد صندوق", dataType = "NUMBER", mandatory = true, filterPosition = 1 },
                        new { label = "تاریخ شروع", dataType = "DATE", mandatory = true, filterPosition = 2 },
                        new { label = "تاریخ پایان", dataType = "DATE", mandatory = true, filterPosition = 3 }
                    }
                }, new JsonSerializerOptions { WriteIndented = true });

                await context.Response.WriteAsync(defaultOptionsJson, Encoding.UTF8);
            }
            return;
        }

        // 4. Handle 404 if no endpoint was matched
        if (matchedEndpoint == null)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = "application/json; charset=utf-8";

            var notFoundResponse = new
            {
                errorCode = 404,
                errorDescription = $"سرویس با متد {reqMethod} در مسیر {reqPath} در لیست سرویس‌های صندوق یافت نشد.",
                availableEndpointsCount = activeEndpoints.Count
            };

            _logger.LogWarning("Endpoint Not Found: [{Method}] {Path}", reqMethod, reqPath);
            await context.Response.WriteAsync(JsonSerializer.Serialize(notFoundResponse, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            return;
        }

        // 5. Check for simulated error via x-mock-response-code header
        string? mockResponseCodeHeader = context.Request.Headers["x-mock-response-code"].FirstOrDefault();
        int responseStatusCode = matchedEndpoint.DefaultStatusCode > 0 ? matchedEndpoint.DefaultStatusCode : 200;
        string responseBodyJson;

        if (!string.IsNullOrWhiteSpace(mockResponseCodeHeader) &&
            (mockResponseCodeHeader.StartsWith("4") || mockResponseCodeHeader.StartsWith("5")) &&
            !string.IsNullOrWhiteSpace(matchedEndpoint.ErrorResponseJson))
        {
            if (int.TryParse(mockResponseCodeHeader, out int parsedCode))
            {
                responseStatusCode = parsedCode;
            }
            else
            {
                responseStatusCode = 400;
            }

            responseBodyJson = matchedEndpoint.ErrorResponseJson;
            _logger.LogInformation("Simulated Error Triggered: [{Method}] {Path} -> {StatusCode}", reqMethod, reqPath, responseStatusCode);
        }
        else
        {
            responseBodyJson = matchedEndpoint.SuccessResponseJson;
            _logger.LogInformation("Success Response: [{Method}] {Path} -> {StatusCode} ({Title})", reqMethod, reqPath, responseStatusCode, matchedEndpoint.Title);
        }

        // 6. Token / Variable replacement (e.g., {{nationalCode}}, {{dsCode}})
        var globalVars = await cacheService.GetGlobalVariablesAsync();
        responseBodyJson = ReplaceVariables(responseBodyJson, globalVars, context);

        // 7. Send final response
        context.Response.StatusCode = responseStatusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(responseBodyJson, Encoding.UTF8);
    }

    private static void ApplyCorsHeaders(HttpContext context)
    {
        var headers = context.Response.Headers;
        if (!headers.ContainsKey("Access-Control-Allow-Origin"))
            headers["Access-Control-Allow-Origin"] = "*";

        if (!headers.ContainsKey("Access-Control-Allow-Methods"))
            headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";

        if (!headers.ContainsKey("Access-Control-Allow-Headers"))
            headers["Access-Control-Allow-Headers"] = "Content-Type, X-CLIENT-TOKEN, X-AUTH-TOKEN, x-mock-response-code, x-mock-response-name";
    }

    private static string ReplaceVariables(string json, IReadOnlyDictionary<string, string> globalVars, HttpContext context)
    {
        if (string.IsNullOrEmpty(json)) return json;

        var sb = new StringBuilder(json);

        // Replace global variables first
        foreach (var (key, value) in globalVars)
        {
            // Allow override from request query or header if passed without curly braces
            string cleanKey = key.Trim('{', '}');
            string overrideVal = context.Request.Headers[cleanKey].FirstOrDefault()
                                ?? context.Request.Query[cleanKey].FirstOrDefault()
                                ?? value;

            sb.Replace(key, overrideVal);
        }

        // Ensure defaults for standard keys if not in globalVars
        sb.Replace("{{nationalCode}}", "0012345678");
        sb.Replace("{{dsCode}}", "11452");

        return sb.ToString();
    }
}
