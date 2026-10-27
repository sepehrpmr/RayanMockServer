using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RayanMockServer.Models;

namespace RayanMockServer.Data;

public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext context, IWebHostEnvironment env)
    {
        // 1. Seed Global Variables if not exists
        if (!await context.GlobalVariables.AnyAsync())
        {
            var defaultVars = new List<GlobalVariable>
            {
                new()
                {
                    Key = "{{nationalCode}}",
                    Value = "0012345678",
                    Description = "کد ملی پیش‌فرض تستر و سرمایه‌گذار",
                    UpdatedAt = DateTime.UtcNow
                },
                new()
                {
                    Key = "{{dsCode}}",
                    Value = "11452",
                    Description = "کد کارگزار / صندوق پیش‌فرض رایان هم‌افزا",
                    UpdatedAt = DateTime.UtcNow
                }
            };
            await context.GlobalVariables.AddRangeAsync(defaultVars);
            await context.SaveChangesAsync();
        }

        // 2. Seed Mock API Endpoints if table is empty
        if (!await context.MockApiEndpoints.AnyAsync())
        {
            string seedFilePath = Path.Combine(env.ContentRootPath, "SeedData", "rayan_routes.json");
            if (File.Exists(seedFilePath))
            {
                string jsonContent = await File.ReadAllTextAsync(seedFilePath);
                await ImportRoutesFromJsonAsync(context, jsonContent, overwriteExisting: false);
            }
        }
    }

    public static async Task<(int inserted, int updated, int failed)> ImportRoutesFromJsonAsync(
        AppDbContext context, 
        string jsonArrayString, 
        bool overwriteExisting = false)
    {
        using var doc = JsonDocument.Parse(jsonArrayString);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("قالب JSON ورودی باید یک آرایه از اشیاء باشد.");
        }

        int inserted = 0;
        int updated = 0;
        int failed = 0;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            try
            {
                int idx = item.TryGetProperty("idx", out var idxProp) ? idxProp.GetInt32() : 0;
                string methodRaw = item.TryGetProperty("method", out var mProp) ? mProp.GetString() ?? "GET" : "GET";
                string urlRaw = item.TryGetProperty("url", out var uProp) ? uProp.GetString() ?? "/" : "/";
                string title = item.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? string.Empty : string.Empty;

                string normalizedMethod = NormalizeMethod(methodRaw);
                string normalizedUrl = NormalizeUrl(urlRaw);
                string categoryTag = DetermineCategoryTag(normalizedUrl);

                string? reqBodyJson = null;
                if (item.TryGetProperty("reqBody", out var reqProp) && reqProp.ValueKind != JsonValueKind.Null)
                {
                    reqBodyJson = reqProp.ValueKind == JsonValueKind.String 
                        ? reqProp.GetString() 
                        : reqProp.GetRawText();
                }

                string successResponseJson = "{}";
                if (item.TryGetProperty("response", out var resProp))
                {
                    if (resProp.ValueKind == JsonValueKind.True) successResponseJson = "true";
                    else if (resProp.ValueKind == JsonValueKind.False) successResponseJson = "false";
                    else if (resProp.ValueKind == JsonValueKind.String) successResponseJson = JsonSerializer.Serialize(resProp.GetString());
                    else if (resProp.ValueKind == JsonValueKind.Number) successResponseJson = resProp.GetRawText();
                    else if (resProp.ValueKind == JsonValueKind.Null) successResponseJson = "null";
                    else successResponseJson = resProp.GetRawText();
                }

                string? errorResponseJson = null;
                if (item.TryGetProperty("errorResponse", out var errProp) && errProp.ValueKind != JsonValueKind.Null)
                {
                    errorResponseJson = errProp.ValueKind == JsonValueKind.String
                        ? errProp.GetString()
                        : errProp.GetRawText();
                }

                // Check if endpoint exists with same method and url
                var existing = await context.MockApiEndpoints
                    .FirstOrDefaultAsync(e => e.HttpMethod == normalizedMethod && e.UrlPath == normalizedUrl && e.IndexNumber == idx);

                if (existing != null)
                {
                    if (overwriteExisting)
                    {
                        existing.Title = title;
                        existing.CategoryTag = categoryTag;
                        existing.RequestBodyJson = reqBodyJson;
                        existing.SuccessResponseJson = successResponseJson;
                        existing.ErrorResponseJson = errorResponseJson;
                        existing.UpdatedAt = DateTime.UtcNow;
                        updated++;
                    }
                }
                else
                {
                    var newEndpoint = new MockApiEndpoint
                    {
                        IndexNumber = idx,
                        Title = title,
                        HttpMethod = normalizedMethod,
                        UrlPath = normalizedUrl,
                        CategoryTag = categoryTag,
                        RequestBodyJson = reqBodyJson,
                        SuccessResponseJson = successResponseJson,
                        ErrorResponseJson = errorResponseJson,
                        DefaultStatusCode = 200,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await context.MockApiEndpoints.AddAsync(newEndpoint);
                    inserted++;
                }
            }
            catch
            {
                failed++;
            }
        }

        await context.SaveChangesAsync();
        return (inserted, updated, failed);
    }

    /// <summary>
    /// نرمال‌سازی متد HTTP: تبدیل OPTION به OPTIONS و حذف فاصله‌های اضافی
    /// </summary>
    public static string NormalizeMethod(string method)
    {
        if (string.IsNullOrWhiteSpace(method)) return "GET";
        string m = method.Trim().ToUpperInvariant();
        if (m == "OPTION") return "OPTIONS";
        return m;
    }

    /// <summary>
    /// نرمال‌سازی مسیر URL: حذف اسلش‌های اضافی انتهایی و اطمینان از وجود اسلش در ابتدا
    /// </summary>
    public static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "/";
        string cleaned = url.Trim();
        if (!cleaned.StartsWith("/"))
        {
            cleaned = "/" + cleaned;
        }

        cleaned = cleaned.TrimEnd('/');
        return string.IsNullOrEmpty(cleaned) ? "/" : cleaned;
    }

    /// <summary>
    /// استخراج هوشمند برچسب دسته‌بندی از روی سگمنت‌های آدرس URL
    /// </summary>
    public static string DetermineCategoryTag(string urlPath)
    {
        if (string.IsNullOrWhiteSpace(urlPath)) return "general";

        var segments = urlPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Examples:
        // /api/v1/fund/nav -> ["api", "v1", "fund", "nav"] => "fund"
        // /api/v1/customers/statements -> "customers"
        // /api/v2/sejam/profiles/... -> "sejam"
        // /api/v1/authenticate -> "auth"
        // /api/v1/branches -> "fund" or "branches"

        foreach (var seg in segments)
        {
            string lower = seg.ToLowerInvariant();
            if (lower == "api" || lower.StartsWith("v") && lower.Length <= 4 && char.IsDigit(lower[^1]))
            {
                continue;
            }

            return lower switch
            {
                "authenticate" or "login" or "logout" or "changepassword" => "احراز هویت (Auth)",
                "fund" or "fundorder" or "fundrevokeorders" or "receipts" or "paymentlist" => "صندوق سرمایه‌گذاری (Fund)",
                "customers" or "allcustomerinfo" or "listcustomers" or "client" => "مشتریان (Customers)",
                "sejam" => "سامانه سجام (Sejam)",
                "user" or "siteuser" or "appusers" => "کاربران (Users)",
                "banks" or "bankaccounttypes" or "bankwebservice" => "بانکی و پرداخت (Banks)",
                "mobile" => "سرویس‌های موبایل (Mobile)",
                "captcha" or "checkcaptchavalidation" => "کپچا و امنیت (Security)",
                "general" => "عمومی (General)",
                _ => lower
            };
        }

        return "عمومی (General)";
    }
}
