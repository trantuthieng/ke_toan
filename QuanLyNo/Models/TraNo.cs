using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanLyNo.Models;

public class TraNo
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Khách hàng")]
    [MaxLength(200)]
    public string TenKhach { get; set; } = "";

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Ngày trả")]
    public DateTime NgayTra { get; set; } = DateTime.Today;

    [Required]
    [Display(Name = "Số tiền trả")]
    [Column(TypeName = "decimal(18,0)")]
    public decimal SoTienTra { get; set; }

    [Display(Name = "Ghi chú")]
    [MaxLength(500)]
    public string? GhiChu { get; set; }
}
