using System.Text.Json;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using RayanMockServer.Models;
using RayanMockServer.Services;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace RayanMockServer.Swagger;

public class SqlSwaggerDocumentFilter : IDocumentFilter
{
    private readonly IMockCacheService _cacheService;
    private readonly ILogger<SqlSwaggerDocumentFilter> _logger;

    public SqlSwaggerDocumentFilter(IMockCacheService cacheService, ILogger<SqlSwaggerDocumentFilter> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        try
        {
            var endpoints = _cacheService.GetActiveEndpointsSync();
            _logger.LogInformation("Applying SqlSwaggerDocumentFilter: Found {Count} active endpoints in SQL database.", endpoints.Count);

            foreach (var endpoint in endpoints)
            {
                try
                {
                    string path = endpoint.UrlPath.TrimEnd('/');
                    if (string.IsNullOrEmpty(path)) path = "/";
                    if (!path.StartsWith("/")) path = "/" + path;

                    if (!swaggerDoc.Paths.TryGetValue(path, out var pathItem))
                    {
                        pathItem = new OpenApiPathItem();
                        swaggerDoc.Paths.Add(path, pathItem);
                    }

                    var opType = MapHttpMethodToOperationType(endpoint.HttpMethod);

                    // Prevent duplicate key collision if multiple records share the same URL and Method
                    if (pathItem.Operations.TryGetValue(opType, out var existingOp))
                    {
                        existingOp.Summary += $" | {endpoint.Title}";
                        existingOp.Description += $"\n\n--- \n**سناریوی جایگزین (ردیف {endpoint.IndexNumber}):** {endpoint.Title}";
                        continue;
                    }

                    var operation = new OpenApiOperation
                    {
                        Summary = endpoint.Title,
                        Description = $"شناسه: {endpoint.IndexNumber} | مسیر: `{endpoint.UrlPath}` | کد وضعیت پیش‌فرض: {endpoint.DefaultStatusCode}",
                        OperationId = $"{endpoint.HttpMethod.ToLowerInvariant()}_{endpoint.IndexNumber}_{endpoint.Id}",
                        Tags = new List<OpenApiTag>
                        {
                            new() { Name = endpoint.CategoryTag }
                        }
                    };

                    // 1. Header parameter for error simulation
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "x-mock-response-code",
                        In = ParameterLocation.Header,
                        Required = false,
                        Description = "کد شبیه‌سازی خطا (مثلاً 400 یا 404) جهت دریافت پاسخ خطا به جای پاسخ موفق",
                        Schema = new OpenApiSchema
                        {
                            Type = "string",
                            Default = new OpenApiString("200")
                        }
                    });

                    // 2. Sample RequestBody for POST, PUT, etc.
                    if (!string.IsNullOrWhiteSpace(endpoint.RequestBodyJson))
                    {
                        var (bodySchema, bodyExample) = CreateSchemaAndExample(endpoint.RequestBodyJson);
                        operation.RequestBody = new OpenApiRequestBody
                        {
                            Description = "نمونه داده ورودی (JSON)",
                            Required = true,
                            Content = new Dictionary<string, OpenApiMediaType>
                            {
                                ["application/json"] = new OpenApiMediaType
                                {
                                    Schema = bodySchema,
                                    Example = bodyExample
                                }
                            }
                        };
                    }

                    // 3. Document 200 OK / Default Status Response
                    string statusCodeStr = endpoint.DefaultStatusCode > 0 ? endpoint.DefaultStatusCode.ToString() : "200";
                    var (successSchema, successExample) = CreateSchemaAndExample(endpoint.SuccessResponseJson);

                    operation.Responses[statusCodeStr] = new OpenApiResponse
                    {
                        Description = "پاسخ موفق سرویس (Success Response)",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = successSchema,
                                Example = successExample
                            }
                        }
                    };

                    // 4. Document 400 Bad Request if ErrorResponseJson is provided
                    if (!string.IsNullOrWhiteSpace(endpoint.ErrorResponseJson))
                    {
                        var (errorSchema, errorExample) = CreateSchemaAndExample(endpoint.ErrorResponseJson);
                        operation.Responses["400"] = new OpenApiResponse
                        {
                            Description = "پاسخ خطا در صورت ارسال هدر x-mock-response-code: 400",
                            Content = new Dictionary<string, OpenApiMediaType>
                            {
                                ["application/json"] = new OpenApiMediaType
                                {
                                    Schema = errorSchema,
                                    Example = errorExample
                                }
                            }
                        };
                    }

                    pathItem.Operations[opType] = operation;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to document endpoint [{Method}] {Path} in Swagger", endpoint.HttpMethod, endpoint.UrlPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error executing SqlSwaggerDocumentFilter");
        }
    }

    private static OperationType MapHttpMethodToOperationType(string method)
    {
        return method.ToUpperInvariant() switch
        {
            "GET" => OperationType.Get,
            "POST" => OperationType.Post,
            "PUT" => OperationType.Put,
            "DELETE" => OperationType.Delete,
            "OPTIONS" => OperationType.Options,
            "PATCH" => OperationType.Patch,
            "HEAD" => OperationType.Head,
            _ => OperationType.Get
        };
    }

    private static (OpenApiSchema schema, IOpenApiAny example) CreateSchemaAndExample(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (new OpenApiSchema { Type = "string" }, new OpenApiString(string.Empty));
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var example = ConvertElement(root);
            var schema = root.ValueKind switch
            {
                JsonValueKind.Array => new OpenApiSchema { Type = "array" },
                JsonValueKind.True or JsonValueKind.False => new OpenApiSchema { Type = "boolean" },
                JsonValueKind.Number => new OpenApiSchema { Type = "number" },
                JsonValueKind.String => new OpenApiSchema { Type = "string" },
                _ => new OpenApiSchema { Type = "object" }
            };

            return (schema, example);
        }
        catch
        {
            return (new OpenApiSchema { Type = "string" }, new OpenApiString(json));
        }
    }

    private static IOpenApiAny ConvertElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ConvertObject(element),
            JsonValueKind.Array => ConvertArray(element),
            JsonValueKind.String => new OpenApiString(element.GetString() ?? string.Empty),
            JsonValueKind.Number => element.TryGetInt64(out long l) 
                ? new OpenApiLong(l) 
                : new OpenApiDouble(element.GetDouble()),
            JsonValueKind.True => new OpenApiBoolean(true),
            JsonValueKind.False => new OpenApiBoolean(false),
            _ => new OpenApiNull()
        };
    }

    private static OpenApiObject ConvertObject(JsonElement element)
    {
        var obj = new OpenApiObject();
        foreach (var prop in element.EnumerateObject())
        {
            obj[prop.Name] = ConvertElement(prop.Value);
        }
        return obj;
    }

    private static OpenApiArray ConvertArray(JsonElement element)
    {
        var arr = new OpenApiArray();
        foreach (var item in element.EnumerateArray())
        {
            arr.Add(ConvertElement(item));
        }
        return arr;
    }
}
