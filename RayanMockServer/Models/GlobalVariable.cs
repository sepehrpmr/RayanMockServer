using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RayanMockServer.Models;

public class GlobalVariable
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// کلید متغیر سراسری مثل {{nationalCode}} یا {{dsCode}}
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// مقدار جایگزین پیش‌فرض
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// توضیحات یا راهنما
    /// </summary>
    [MaxLength(250)]
    public string? Description { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
