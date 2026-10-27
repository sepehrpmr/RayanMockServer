using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RayanMockServer.Models;

public class MockApiEndpoint
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// معادل فیلد idx در دیتای اولیه شرکت
    /// </summary>
    public int IndexNumber { get; set; }

    /// <summary>
    /// عنوان فارسی وب‌سرویس
    /// </summary>
    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// متد HTTP (GET, POST, PUT, DELETE, OPTIONS)
    /// </summary>
    [Required]
    [MaxLength(15)]
    [Column(TypeName = "varchar(15)")]
    public string HttpMethod { get; set; } = "GET";

    /// <summary>
    /// مسیر سرویس به صورت نرمالایز شده و بدون اسلش انتهایی (مثلاً api/v1/fund/nav/)
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string UrlPath { get; set; } = string.Empty;

    /// <summary>
    /// دسته‌بندی خودکار سرویس برای گروه‌بندی در مستندات Swagger
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string CategoryTag { get; set; } = "general";

    /// <summary>
    /// نمونه JSON ورودی متد (RequestBody)
    /// </summary>
    public string? RequestBodyJson { get; set; }

    /// <summary>
    /// پاسخ موفق سرویس (Success Response)
    /// </summary>
    [Required]
    public string SuccessResponseJson { get; set; } = "{}";

    /// <summary>
    /// پاسخ خطای شبیه‌سازی شده (Error Response)
    /// </summary>
    public string? ErrorResponseJson { get; set; }

    /// <summary>
    /// کد وضعیت HTTP پیش‌فرض (پیش‌فرض 200)
    /// </summary>
    public int DefaultStatusCode { get; set; } = 200;

    /// <summary>
    /// فعال یا غیرفعال بودن وب‌سرویس
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
