using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanLyNo.Models;

public class KhachHang
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Tên khách hàng")]
    [MaxLength(200)]
    public string TenKhach { get; set; } = "";

    [Display(Name = "Nợ cũ")]
    [Column(TypeName = "decimal(18,0)")]
    public decimal NoCu { get; set; }

    [Display(Name = "Trả nợ cũ")]
    [Column(TypeName = "decimal(18,0)")]
    public decimal TraNoCu { get; set; }

    [Display(Name = "Ghi chú")]
    [MaxLength(500)]
    public string? GhiChu { get; set; }
}
