using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanLyNo.Models;

public class GiaoDich
{
    public int Id { get; set; }

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Ngày")]
    public DateTime Ngay { get; set; } = DateTime.Today;

    [Display(Name = "Khách hàng mua")]
    [MaxLength(200)]
    public string TenKhach { get; set; } = "";

    [Display(Name = "Lái (khách bán)")]
    [MaxLength(200)]
    public string? TenLai { get; set; }

    [Display(Name = "Số con (SC)")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal SoCon { get; set; }

    [Required]
    [Display(Name = "Số lượng (kg)")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal SoLuong { get; set; }

    [Required]
    [Display(Name = "Giá")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Gia { get; set; }

    [Required]
    [Display(Name = "Thành tiền")]
    [Column(TypeName = "decimal(18,0)")]
    public decimal ThanhTien { get; set; }

    [Display(Name = "Tiền trả lái")]
    [Column(TypeName = "decimal(18,0)")]
    public decimal TienTraLai { get; set; }

    [Display(Name = "Ghi chú")]
    [MaxLength(500)]
    public string? GhiChu { get; set; }
}
