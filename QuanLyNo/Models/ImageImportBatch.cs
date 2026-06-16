using System.ComponentModel.DataAnnotations;

namespace QuanLyNo.Models;

public class ImageImportBatch
{
    public int Id { get; set; }

    [DataType(DataType.Date)]
    public DateTime Ngay { get; set; } = DateTime.Today;

    [MaxLength(50)]
    public string LoaiImport { get; set; } = ImageImportTypes.NhapNoMoi;

    [MaxLength(50)]
    public string NguonBanHang { get; set; } = "";

    [MaxLength(260)]
    public string OriginalFileName { get; set; } = "";

    [MaxLength(260)]
    public string StoredFileName { get; set; } = "";

    [MaxLength(500)]
    public string ImagePath { get; set; } = "";

    [MaxLength(50)]
    public string Status { get; set; } = ImageImportStatuses.NeedsReview;

    public string? RawText { get; set; }

    public string? ParseError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    public List<ImageImportRow> Rows { get; set; } = new();
}

public static class ImageImportTypes
{
    public const string NhapNoMoi = "NhapNoMoi";
    public const string TraNoHomNay = "TraNoHomNay";
}

public static class ImageImportStatuses
{
    public const string Uploaded = "uploaded";
    public const string Parsed = "parsed";
    public const string NeedsReview = "needs_review";
    public const string Applied = "applied";
    public const string Failed = "failed";
}

public static class ImageImportReviewStatuses
{
    public const string Matched = "matched";
    public const string Mismatch = "mismatch";
    public const string NeedsReview = "needs_review";
    public const string Confirmed = "confirmed";
    public const string Applied = "applied";
}
