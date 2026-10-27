using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RayanMockServer.Data;
using RayanMockServer.Models;
using RayanMockServer.Services;

namespace RayanMockServer.Controllers;

[ApiController]
[Route("api/admin-mgmt")]
public class AdminApiController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IMockCacheService _cacheService;
    private readonly ILogger<AdminApiController> _logger;

    public AdminApiController(
        AppDbContext context,
        IMockCacheService cacheService,
        ILogger<AdminApiController> logger)
    {
        _context = context;
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>
    /// دریافت آمار کلی و داشبورد سرویس‌ها
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var total = await _context.MockApiEndpoints.CountAsync();
        var active = await _context.MockApiEndpoints.CountAsync(e => e.IsActive);
        var categories = await _context.MockApiEndpoints
            .Select(e => e.CategoryTag)
            .Distinct()
            .ToListAsync();

        var methodCounts = await _context.MockApiEndpoints
            .GroupBy(e => e.HttpMethod)
            .Select(g => new { Method = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Method, x => x.Count);

        return Ok(new
        {
            totalEndpoints = total,
            activeEndpoints = active,
            inactiveEndpoints = total - active,
            categories,
            methodCounts
        });
    }

    /// <summary>
    /// دریافت فهرست کامل اندپوینت‌ها با امکان جستجو و فیلتر
    /// </summary>
    [HttpGet("endpoints")]
    public async Task<IActionResult> GetEndpoints(
        [FromQuery] string? search = null,
        [FromQuery] string? method = null,
        [FromQuery] string? category = null,
        [FromQuery] bool? isActive = null)
    {
        var query = _context.MockApiEndpoints.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            query = query.Where(e => e.Title.Contains(s) || e.UrlPath.Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(method) && method != "ALL")
        {
            query = query.Where(e => e.HttpMethod == method.ToUpperInvariant());
        }

        if (!string.IsNullOrWhiteSpace(category) && category != "ALL")
        {
            query = query.Where(e => e.CategoryTag == category);
        }

        if (isActive.HasValue)
        {
            query = query.Where(e => e.IsActive == isActive.Value);
        }

        var list = await query
            .OrderBy(e => e.IndexNumber)
            .ThenBy(e => e.Id)
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>
    /// دریافت جزئیات یک اندپوینت بر اساس شناسه
    /// </summary>
    [HttpGet("endpoints/{id:int}")]
    public async Task<IActionResult> GetEndpointById(int id)
    {
        var endpoint = await _context.MockApiEndpoints.FindAsync(id);
        if (endpoint == null) return NotFound(new { message = "اندپوینت مورد نظر یافت نشد." });
        return Ok(endpoint);
    }

    /// <summary>
    /// ایجاد اندپوینت جدید
    /// </summary>
    [HttpPost("endpoints")]
    public async Task<IActionResult> CreateEndpoint([FromBody] MockApiEndpoint model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
            return BadRequest(new { message = "عنوان فارسی وب‌سرویس الزامی است." });

        if (string.IsNullOrWhiteSpace(model.UrlPath))
            return BadRequest(new { message = "مسیر آدرس URL الزامی است." });

        model.HttpMethod = DataSeeder.NormalizeMethod(model.HttpMethod);
        model.UrlPath = DataSeeder.NormalizeUrl(model.UrlPath);

        if (string.IsNullOrWhiteSpace(model.CategoryTag))
        {
            model.CategoryTag = DataSeeder.DetermineCategoryTag(model.UrlPath);
        }

        if (model.IndexNumber <= 0)
        {
            int maxIdx = await _context.MockApiEndpoints.MaxAsync(e => (int?)e.IndexNumber) ?? 0;
            model.IndexNumber = maxIdx + 1;
        }

        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;

        _context.MockApiEndpoints.Add(model);
        await _context.SaveChangesAsync();

        _cacheService.InvalidateCache();
        return CreatedAtAction(nameof(GetEndpointById), new { id = model.Id }, model);
    }

    /// <summary>
    /// ویرایش اندپوینت موجود
    /// </summary>
    [HttpPut("endpoints/{id:int}")]
    public async Task<IActionResult> UpdateEndpoint(int id, [FromBody] MockApiEndpoint model)
    {
        var existing = await _context.MockApiEndpoints.FindAsync(id);
        if (existing == null) return NotFound(new { message = "اندپوینت مورد نظر یافت نشد." });

        existing.Title = model.Title;
        existing.IndexNumber = model.IndexNumber;
        existing.HttpMethod = DataSeeder.NormalizeMethod(model.HttpMethod);
        existing.UrlPath = DataSeeder.NormalizeUrl(model.UrlPath);
        existing.CategoryTag = string.IsNullOrWhiteSpace(model.CategoryTag)
            ? DataSeeder.DetermineCategoryTag(existing.UrlPath)
            : model.CategoryTag;

        existing.RequestBodyJson = model.RequestBodyJson;
        existing.SuccessResponseJson = model.SuccessResponseJson;
        existing.ErrorResponseJson = model.ErrorResponseJson;
        existing.DefaultStatusCode = model.DefaultStatusCode > 0 ? model.DefaultStatusCode : 200;
        existing.IsActive = model.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _cacheService.InvalidateCache();

        return Ok(existing);
    }

    /// <summary>
    /// فعال یا غیرفعال‌سازی سریع وب‌سرویس
    /// </summary>
    [HttpPatch("endpoints/{id:int}/toggle-status")]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var existing = await _context.MockApiEndpoints.FindAsync(id);
        if (existing == null) return NotFound(new { message = "اندپوینت مورد نظر یافت نشد." });

        existing.IsActive = !existing.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _cacheService.InvalidateCache();

        return Ok(new { id = existing.Id, isActive = existing.IsActive });
    }

    /// <summary>
    /// حذف یک اندپوینت
    /// </summary>
    [HttpDelete("endpoints/{id:int}")]
    public async Task<IActionResult> DeleteEndpoint(int id)
    {
        var existing = await _context.MockApiEndpoints.FindAsync(id);
        if (existing == null) return NotFound(new { message = "اندپوینت مورد نظر یافت نشد." });

        _context.MockApiEndpoints.Remove(existing);
        await _context.SaveChangesAsync();
        _cacheService.InvalidateCache();

        return Ok(new { message = "اندپوینت با موفقیت حذف شد." });
    }

    /// <summary>
    /// ایمپورت دسته‌ای آرایه JSON سرویس‌های شرکت
    /// </summary>
    [HttpPost("import-batch")]
    public async Task<IActionResult> ImportBatch([FromBody] BatchImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JsonData))
        {
            return BadRequest(new { message = "محتوای JSON ارسال شده خالی است." });
        }

        try
        {
            var result = await DataSeeder.ImportRoutesFromJsonAsync(_context, request.JsonData, request.OverwriteExisting);
            _cacheService.InvalidateCache();

            return Ok(new
            {
                message = $"عملیات ایمپورت با موفقیت انجام شد: {result.inserted} رکورد جدید اضافه شد، {result.updated} رکورد به‌روزرسانی شد و {result.failed} خطا رخ داد.",
                result.inserted,
                result.updated,
                result.failed
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing batch JSON");
            return BadRequest(new { message = $"خطا در خواندن یا پردازش JSON: {ex.Message}" });
        }
    }

    /// <summary>
    /// دریافت لیست متغیرهای سراسری
    /// </summary>
    [HttpGet("variables")]
    public async Task<IActionResult> GetVariables()
    {
        var list = await _context.GlobalVariables
            .AsNoTracking()
            .OrderBy(v => v.Id)
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>
    /// ایجاد یا به‌روزرسانی متغیر سراسری
    /// </summary>
    [HttpPost("variables")]
    public async Task<IActionResult> SaveVariable([FromBody] GlobalVariable model)
    {
        if (string.IsNullOrWhiteSpace(model.Key) || string.IsNullOrWhiteSpace(model.Value))
            return BadRequest(new { message = "کلید و مقدار متغیر الزامی هستند." });

        // Ensure key format has {{ ... }}
        string key = model.Key.Trim();
        if (!key.StartsWith("{{")) key = "{{" + key;
        if (!key.EndsWith("}}")) key = key + "}}";
        model.Key = key;

        var existing = await _context.GlobalVariables.FirstOrDefaultAsync(v => v.Key == model.Key);
        if (existing != null)
        {
            existing.Value = model.Value;
            existing.Description = model.Description;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            model.UpdatedAt = DateTime.UtcNow;
            _context.GlobalVariables.Add(model);
        }

        await _context.SaveChangesAsync();
        _cacheService.InvalidateCache();

        return Ok(model);
    }

    /// <summary>
    /// حذف متغیر سراسری
    /// </summary>
    [HttpDelete("variables/{id:int}")]
    public async Task<IActionResult> DeleteVariable(int id)
    {
        var existing = await _context.GlobalVariables.FindAsync(id);
        if (existing == null) return NotFound(new { message = "متغیر یافت نشد." });

        _context.GlobalVariables.Remove(existing);
        await _context.SaveChangesAsync();
        _cacheService.InvalidateCache();

        return Ok(new { message = "متغیر با موفقیت حذف شد." });
    }
}

public class BatchImportRequest
{
    public string JsonData { get; set; } = string.Empty;
    public bool OverwriteExisting { get; set; } = true;
}
