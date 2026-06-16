using Microsoft.EntityFrameworkCore;
using QuanLyNo.Data;
using QuanLyNo.Models;

namespace QuanLyNo.Services;

public class ImageImportService
{
    private const decimal QuantityTolerance = 0.1m;

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly GeminiImageParser _parser;

    public ImageImportService(AppDbContext db, IWebHostEnvironment env, IConfiguration configuration)
    {
        _db = db;
        _env = env;
        _configuration = configuration;
        _parser = new GeminiImageParser(configuration);
    }

    public async Task<List<ImageImportBatch>> CreateBatchesAsync(IEnumerable<IFormFile> files, DateTime ngay,
        string? loaiImport, string? nguonBanHang, CancellationToken cancellationToken = default)
    {
        var batches = new List<ImageImportBatch>();
        var baseType = NormalizeImportType(loaiImport);
        var baseSource = CleanText(nguonBanHang) ?? "";

        foreach (var file in files.Where(f => f.Length > 0 && IsImage(f)))
        {
            var effectiveType = baseType;
            var effectiveSource = baseSource;
            InferFromFilename(file.FileName, ref effectiveType, ref effectiveSource);
            var batch = await CreateBatchAsync(file, ngay.Date, effectiveType, effectiveSource, cancellationToken);
            batches.Add(batch);
            _db.ImageImportBatches.Add(batch);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return batches;
    }

    public async Task<List<ImageImportBatch>> GetBatchesAsync(DateTime? ngay, string? loaiImport, string? nguonBanHang,
        CancellationToken cancellationToken = default)
    {
        var query = _db.ImageImportBatches
            .Include(b => b.Rows)
                .ThenInclude(r => r.MatchedGiaoDich)
            .Include(b => b.Rows)
                .ThenInclude(r => r.MatchedTraNo)
            .AsNoTracking()
            .AsQueryable();

        if (ngay.HasValue)
            query = query.Where(b => b.Ngay.Date == ngay.Value.Date);

        if (!string.IsNullOrWhiteSpace(loaiImport))
        {
            var normalizedType = NormalizeImportType(loaiImport);
            query = query.Where(b => b.LoaiImport == normalizedType);
        }

        if (!string.IsNullOrWhiteSpace(nguonBanHang))
        {
            var source = CleanText(nguonBanHang) ?? "";
            query = query.Where(b => b.NguonBanHang == source);
        }

        return await query
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<ImageImportApplyResult> ApplyReviewAsync(ImageImportReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new ImageImportApplyResult();
        if (request.Rows.Count == 0)
            return result;

        var rowIds = request.Rows.Select(r => r.RowId).Distinct().ToList();
        var rows = await _db.ImageImportRows
            .Include(r => r.Batch)
            .Where(r => rowIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        foreach (var item in request.Rows)
        {
            if (!rows.TryGetValue(item.RowId, out var row) || row.Batch == null)
            {
                result.Skipped++;
                continue;
            }

            UpdateReviewRow(row, item);
            if (!item.Confirmed)
            {
                row.ReviewStatus = ImageImportReviewStatuses.NeedsReview;
                result.Pending++;
                continue;
            }

            if (row.Batch.LoaiImport == ImageImportTypes.TraNoHomNay)
                await ApplyTraNoRowAsync(row, item, result, cancellationToken);
            else
                await ApplyNhapNoRowAsync(row, item, result, cancellationToken);
        }

        await UpdateBatchStatusesAsync(rows.Values.Select(r => r.ImageImportBatchId).Distinct(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<ImageImportBatch> CreateBatchAsync(IFormFile file, DateTime ngay, string loaiImport,
        string nguonBanHang, CancellationToken cancellationToken)
    {
        var uploadsDir = Path.Combine(GetUploadsRoot(), "image-imports");
        Directory.CreateDirectory(uploadsDir);

        var storedFileName = BuildSafeFileName(file.FileName);
        var filePath = Path.Combine(uploadsDir, storedFileName);
        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var parseResult = await _parser.ParseAsync(filePath, file.ContentType, loaiImport, cancellationToken);
        var batch = new ImageImportBatch
        {
            Ngay = ngay,
            LoaiImport = loaiImport,
            NguonBanHang = nguonBanHang,
            OriginalFileName = file.FileName,
            StoredFileName = storedFileName,
            ImagePath = "/uploads/image-imports/" + storedFileName,
            RawText = parseResult.RawText,
            ParseError = parseResult.Error,
            Status = parseResult.Rows?.Count > 0
                ? ImageImportStatuses.Parsed
                : ImageImportStatuses.NeedsReview
        };

        var rows = parseResult.Rows ?? new List<ImageParseRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            var parsedRow = rows[i];
            batch.Rows.Add(new ImageImportRow
            {
                ImageOrder = parsedRow.ImageOrder > 0 ? parsedRow.ImageOrder : i + 1,
                TenLai = CleanText(parsedRow.TenLai),
                TenKhach = CleanText(parsedRow.TenKhach),
                SoLuongAnh = parsedRow.SoLuongAnh,
                SoTienTra = parsedRow.SoTienTra,
                Confidence = parsedRow.Confidence,
                RawLine = parsedRow.RawLine,
                ReviewStatus = ImageImportReviewStatuses.NeedsReview
            });
        }

        await MatchRowsAsync(batch, cancellationToken);
        return batch;
    }

    private string GetUploadsRoot()
    {
        var configured = _configuration["Uploads:RootPath"] ?? Environment.GetEnvironmentVariable("UPLOADS_ROOT");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(_env.WebRootPath, "uploads")
            : configured;
    }

    private async Task MatchRowsAsync(ImageImportBatch batch, CancellationToken cancellationToken)
    {
        if (batch.LoaiImport == ImageImportTypes.TraNoHomNay)
        {
            await MatchTraNoRowsAsync(batch, cancellationToken);
            return;
        }

        var candidates = await _db.GiaoDichs
            .Where(g => g.Ngay.Date == batch.Ngay.Date)
            .OrderBy(g => g.ImageOrder ?? g.Id)
            .ThenBy(g => g.Id)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(batch.NguonBanHang))
        {
            candidates = candidates
                .Where(g => string.IsNullOrWhiteSpace(g.NguonBanHang) || g.NguonBanHang == batch.NguonBanHang)
                .ToList();
        }

        var used = new HashSet<int>();
        var orderedRows = batch.Rows.OrderBy(r => r.ImageOrder).ToList();
        for (var index = 0; index < orderedRows.Count; index++)
        {
            var row = orderedRows[index];
            var best = candidates
                .Where(c => !used.Contains(c.Id))
                .Select(c => new { Candidate = c, Score = ScoreGiaoDichMatch(row, c, index) })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => Math.Abs((x.Candidate.ImageOrder ?? x.Candidate.Id) - row.ImageOrder))
                .FirstOrDefault();

            if (best == null || best.Score <= 0)
            {
                if (index < candidates.Count && !used.Contains(candidates[index].Id))
                    best = new { Candidate = candidates[index], Score = 1 };
                else
                    continue;
            }

            var candidate = best.Candidate;
            used.Add(candidate.Id);
            row.MatchedGiaoDichId = candidate.Id;
            row.ReviewStatus = GetNhapNoReviewStatus(row, candidate);
            row.Notes = BuildNhapNoNotes(row, candidate);
        }
    }

    private async Task MatchTraNoRowsAsync(ImageImportBatch batch, CancellationToken cancellationToken)
    {
        var existing = await _db.TraNos
            .Where(t => t.NgayTra.Date == batch.Ngay.Date)
            .ToListAsync(cancellationToken);

        foreach (var row in batch.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.TenKhach))
                continue;

            var match = existing.FirstOrDefault(t => SameName(t.TenKhach, row.TenKhach));
            if (match == null)
                continue;

            row.MatchedTraNoId = match.Id;
            row.ReviewStatus = row.SoTienTra.HasValue && Math.Abs(match.SoTienTra - row.SoTienTra.Value) > 0
                ? ImageImportReviewStatuses.Mismatch
                : ImageImportReviewStatuses.Matched;
        }
    }

    private static int ScoreGiaoDichMatch(ImageImportRow row, GiaoDich candidate, int rowIndex)
    {
        var score = 0;
        if (row.SoLuongAnh.HasValue)
        {
            var diff = Math.Abs(candidate.SoLuong - row.SoLuongAnh.Value);
            if (diff <= QuantityTolerance) score += 50;
            else if (diff <= 0.5m) score += 20;
            else score -= 25;
        }

        if (!string.IsNullOrWhiteSpace(row.TenKhach))
        {
            if (SameName(candidate.TenKhach, row.TenKhach)) score += 20;
            else if (string.IsNullOrWhiteSpace(candidate.TenKhach)) score += 8;
        }

        if (!string.IsNullOrWhiteSpace(row.TenLai) && SameName(candidate.TenLai, row.TenLai))
            score += 10;

        if (candidate.ImageOrder.HasValue)
            score += Math.Max(0, 8 - Math.Abs(candidate.ImageOrder.Value - row.ImageOrder));
        else
            score += Math.Max(0, 4 - Math.Abs(candidate.Id - (rowIndex + 1)));

        return score;
    }

    private static string GetNhapNoReviewStatus(ImageImportRow row, GiaoDich candidate)
    {
        if (!row.SoLuongAnh.HasValue)
            return ImageImportReviewStatuses.NeedsReview;

        if (Math.Abs(candidate.SoLuong - row.SoLuongAnh.Value) > QuantityTolerance)
            return ImageImportReviewStatuses.Mismatch;

        if (row.Confidence.HasValue && row.Confidence.Value < 0.75m)
            return ImageImportReviewStatuses.NeedsReview;

        if (string.IsNullOrWhiteSpace(row.TenKhach) && string.IsNullOrWhiteSpace(candidate.TenKhach))
            return ImageImportReviewStatuses.NeedsReview;

        return ImageImportReviewStatuses.Matched;
    }

    private static string? BuildNhapNoNotes(ImageImportRow row, GiaoDich candidate)
    {
        if (!row.SoLuongAnh.HasValue)
            return "Chua doc duoc SL tu anh.";

        var diff = Math.Abs(candidate.SoLuong - row.SoLuongAnh.Value);
        if (diff > QuantityTolerance)
            return $"Lech SL Excel {candidate.SoLuong:#,##0.##} va anh {row.SoLuongAnh:#,##0.##}.";

        return null;
    }

    private async Task ApplyNhapNoRowAsync(ImageImportRow row, ImageImportReviewRowRequest item,
        ImageImportApplyResult result, CancellationToken cancellationToken)
    {
        var giaoDichId = item.MatchedGiaoDichId ?? row.MatchedGiaoDichId;
        if (!giaoDichId.HasValue)
        {
            row.ReviewStatus = ImageImportReviewStatuses.NeedsReview;
            result.Skipped++;
            return;
        }

        var giaoDich = await _db.GiaoDichs.FindAsync(new object?[] { giaoDichId.Value }, cancellationToken);
        if (giaoDich == null)
        {
            row.ReviewStatus = ImageImportReviewStatuses.NeedsReview;
            result.Skipped++;
            return;
        }

        if (!string.IsNullOrWhiteSpace(row.TenKhach))
        {
            giaoDich.TenKhach = row.TenKhach.Trim();
            await EnsureKhachHangPendingAsync(giaoDich.TenKhach, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(row.TenLai))
            giaoDich.TenLai = row.TenLai.Trim();

        giaoDich.SoLuongAnh = row.SoLuongAnh;
        giaoDich.NguonBanHang = row.Batch?.NguonBanHang;
        giaoDich.ImageOrder = row.ImageOrder;
        giaoDich.ImageImportRowId = row.Id;
        giaoDich.ReviewStatus = row.SoLuongAnh.HasValue &&
            Math.Abs(giaoDich.SoLuong - row.SoLuongAnh.Value) > QuantityTolerance
                ? ImageImportReviewStatuses.Mismatch
                : ImageImportReviewStatuses.Matched;

        row.MatchedGiaoDichId = giaoDich.Id;
        row.ReviewStatus = ImageImportReviewStatuses.Applied;
        row.UpdatedAt = DateTime.Now;
        result.Applied++;
    }

    private async Task ApplyTraNoRowAsync(ImageImportRow row, ImageImportReviewRowRequest item,
        ImageImportApplyResult result, CancellationToken cancellationToken)
    {
        var tenKhach = CleanText(row.TenKhach);
        var amount = row.SoTienTra ?? 0;
        if (string.IsNullOrWhiteSpace(tenKhach) || amount < 0)
        {
            row.ReviewStatus = ImageImportReviewStatuses.NeedsReview;
            result.Skipped++;
            return;
        }

        TraNo? traNo = null;
        var traNoId = item.MatchedTraNoId ?? row.MatchedTraNoId;
        if (traNoId.HasValue)
            traNo = await _db.TraNos.FindAsync(new object?[] { traNoId.Value }, cancellationToken);

        if (traNo == null)
        {
            traNo = await _db.TraNos
                .FirstOrDefaultAsync(t => t.NgayTra.Date == row.Batch!.Ngay.Date && t.TenKhach == tenKhach,
                    cancellationToken);
        }

        if (traNo == null)
        {
            traNo = new TraNo
            {
                NgayTra = row.Batch!.Ngay.Date,
                TenKhach = tenKhach,
                GhiChu = "Import tu anh"
            };
            _db.TraNos.Add(traNo);
        }

        traNo.TenKhach = tenKhach;
        traNo.TenLai = row.TenLai;
        traNo.SoTienTra = amount;
        traNo.NguonBanHang = row.Batch!.NguonBanHang;
        traNo.ImageOrder = row.ImageOrder;
        traNo.ImageImportRowId = row.Id;
        traNo.ReviewStatus = ImageImportReviewStatuses.Applied;

        await EnsureKhachHangPendingAsync(tenKhach, cancellationToken);

        row.MatchedTraNo = traNo;
        row.MatchedTraNoId = traNo.Id == 0 ? row.MatchedTraNoId : traNo.Id;
        row.ReviewStatus = ImageImportReviewStatuses.Applied;
        row.UpdatedAt = DateTime.Now;
        result.Applied++;
    }

    private void UpdateReviewRow(ImageImportRow row, ImageImportReviewRowRequest item)
    {
        row.TenLai = CleanText(item.TenLai) ?? row.TenLai;
        row.TenKhach = CleanText(item.TenKhach) ?? row.TenKhach;
        row.SoLuongAnh = item.SoLuongAnh ?? row.SoLuongAnh;
        row.SoTienTra = item.SoTienTra ?? row.SoTienTra;
        row.MatchedGiaoDichId = item.MatchedGiaoDichId ?? row.MatchedGiaoDichId;
        row.MatchedTraNoId = item.MatchedTraNoId ?? row.MatchedTraNoId;
        row.Notes = item.Notes ?? row.Notes;
        row.UpdatedAt = DateTime.Now;
    }

    private async Task UpdateBatchStatusesAsync(IEnumerable<int> batchIds, CancellationToken cancellationToken)
    {
        var ids = batchIds.ToList();
        var batches = await _db.ImageImportBatches
            .Include(b => b.Rows)
            .Where(b => ids.Contains(b.Id))
            .ToListAsync(cancellationToken);

        foreach (var batch in batches)
        {
            batch.Status = batch.Rows.Count > 0 &&
                batch.Rows.All(r => r.ReviewStatus == ImageImportReviewStatuses.Applied)
                    ? ImageImportStatuses.Applied
                    : ImageImportStatuses.NeedsReview;
            batch.UpdatedAt = DateTime.Now;
        }
    }

    private async Task EnsureKhachHangPendingAsync(string tenKhach, CancellationToken cancellationToken)
    {
        tenKhach = CleanText(tenKhach) ?? "";
        if (string.IsNullOrWhiteSpace(tenKhach))
            return;

        var exists = await _db.KhachHangs
            .AnyAsync(k => k.TenKhach.ToUpper() == tenKhach.ToUpper(), cancellationToken);
        if (!exists)
            _db.KhachHangs.Add(new KhachHang { TenKhach = tenKhach });
    }

    private static bool IsImage(IFormFile file)
    {
        if (file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp";
    }

    private static string BuildSafeFileName(string originalFileName)
    {
        var name = Path.GetFileNameWithoutExtension(originalFileName);
        var safeName = new string(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        safeName = string.IsNullOrWhiteSpace(safeName) ? "image" : safeName;
        var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        return $"{safeName}_{DateTime.Now:yyyyMMddHHmmssfff}{ext}";
    }

    private static string NormalizeImportType(string? loaiImport)
    {
        if (string.Equals(loaiImport, ImageImportTypes.TraNoHomNay, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(loaiImport, "tra-no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(loaiImport, "trano", StringComparison.OrdinalIgnoreCase))
            return ImageImportTypes.TraNoHomNay;

        return ImageImportTypes.NhapNoMoi;
    }

    /// <summary>
    /// Nếu loaiImport hoặc nguonBanHang chưa được truyền, tự suy ra từ tên file:
    /// bh1* → NhapNoMoi/BH1 | bh2* → NhapNoMoi/BH2 | xa1* → TraNoHomNay/BH1 | xa2* → TraNoHomNay/BH2
    /// </summary>
    private static void InferFromFilename(string fileName, ref string loaiImport, ref string nguonBanHang)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

        static bool Match(string name, string prefix) =>
            name.StartsWith(prefix, StringComparison.Ordinal) ||
            name.Contains("-" + prefix, StringComparison.Ordinal) ||
            name.Contains("_" + prefix, StringComparison.Ordinal);

        if (Match(baseName, "bh1"))
        {
            if (string.IsNullOrWhiteSpace(loaiImport)) loaiImport = ImageImportTypes.NhapNoMoi;
            if (string.IsNullOrWhiteSpace(nguonBanHang)) nguonBanHang = "BH1";
        }
        else if (Match(baseName, "bh2"))
        {
            if (string.IsNullOrWhiteSpace(loaiImport)) loaiImport = ImageImportTypes.NhapNoMoi;
            if (string.IsNullOrWhiteSpace(nguonBanHang)) nguonBanHang = "BH2";
        }
        else if (Match(baseName, "xa1"))
        {
            if (string.IsNullOrWhiteSpace(loaiImport)) loaiImport = ImageImportTypes.TraNoHomNay;
            if (string.IsNullOrWhiteSpace(nguonBanHang)) nguonBanHang = "BH1";
        }
        else if (Match(baseName, "xa2"))
        {
            if (string.IsNullOrWhiteSpace(loaiImport)) loaiImport = ImageImportTypes.TraNoHomNay;
            if (string.IsNullOrWhiteSpace(nguonBanHang)) nguonBanHang = "BH2";
        }
    }

    private static string? CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool SameName(string? left, string? right)
    {
        var a = CleanText(left);
        var b = CleanText(right);
        if (a == null || b == null)
            return false;

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

public class ImageImportReviewRequest
{
    public List<ImageImportReviewRowRequest> Rows { get; set; } = new();
}

public class ImageImportReviewRowRequest
{
    public int RowId { get; set; }
    public int? MatchedGiaoDichId { get; set; }
    public int? MatchedTraNoId { get; set; }
    public string? TenLai { get; set; }
    public string? TenKhach { get; set; }
    public decimal? SoLuongAnh { get; set; }
    public decimal? SoTienTra { get; set; }
    public string? Notes { get; set; }
    public bool Confirmed { get; set; }
}

public class ImageImportApplyResult
{
    public int Applied { get; set; }
    public int Pending { get; set; }
    public int Skipped { get; set; }
}
