using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanLyNo.Models;

public class ImageImportRow
{
    public int Id { get; set; }

    public int ImageImportBatchId { get; set; }

    public ImageImportBatch? Batch { get; set; }

    public int ImageOrder { get; set; }

    [MaxLength(200)]
    public string? TenLai { get; set; }

    [MaxLength(200)]
    public string? TenKhach { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? SoLuongAnh { get; set; }

    [Column(TypeName = "decimal(18,0)")]
    public decimal? SoTienTra { get; set; }

    [Column(TypeName = "decimal(5,4)")]
    public decimal? Confidence { get; set; }

    public string? RawLine { get; set; }

    public int? MatchedGiaoDichId { get; set; }

    public GiaoDich? MatchedGiaoDich { get; set; }

    public int? MatchedTraNoId { get; set; }

    public TraNo? MatchedTraNo { get; set; }

    [MaxLength(50)]
    public string ReviewStatus { get; set; } = ImageImportReviewStatuses.NeedsReview;

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }
}
