using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuanLyNo.Data;
using QuanLyNo.Models;
using QuanLyNo.Services;
using QuanLyNo.ViewModels;
using System.Text.Json;

namespace QuanLyNo.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;
    private readonly ExcelService _excel = new();
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ImageImportService _imageImports;

    public HomeController(AppDbContext db, IWebHostEnvironment env, IConfiguration configuration)
    {
        _db = db;
        _env = env;
        _configuration = configuration;
        _imageImports = new ImageImportService(db, env, configuration);
    }

    public async Task<IActionResult> Index(DateTime? ngay)
    {
        var today = ngay ?? DateTime.Today;

        // Tab 1: Giao dịch (nợ mới) hôm nay
        var noMoiHomNay = await _db.GiaoDichs
            .Where(g => g.Ngay.Date == today.Date)
            .OrderBy(g => g.Id)
            .ToListAsync();

        // Tab 2: Trả nợ hôm nay
        var noTraHomNay = await _db.TraNos
            .Where(t => t.NgayTra.Date == today.Date)
            .OrderByDescending(t => t.Id)
            .ToListAsync();

        // Tab 3: Thống kê nợ theo khách (báo cáo dạng bảng theo ngày)
        var khachHangs = await _db.KhachHangs.ToListAsync();
        var allGD = await _db.GiaoDichs.ToListAsync();
        var allTN = await _db.TraNos.ToListAsync();

        // Collect all distinct dates that have data, always include today
        var cacNgay = allGD.Select(g => g.Ngay.Date)
            .Union(allTN.Select(t => t.NgayTra.Date))
            .Union(new[] { today.Date })
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // Get all customer names (from KhachHang + from GiaoDich + from TraNo)
        var allNames = khachHangs.Select(k => k.TenKhach)
            .Union(allGD.Select(g => g.TenKhach))
            .Union(allGD.Select(g => g.TenLai ?? ""))
            .Union(allTN.Select(t => t.TenKhach))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        // Group GD and TN by (name, date) for fast lookup
        var gdByNameDate = allGD
            .GroupBy(g => (g.TenKhach.ToUpperInvariant(), g.Ngay.Date))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.ThanhTien));
        var tnByNameDate = allTN
            .GroupBy(t => (t.TenKhach.ToUpperInvariant(), t.NgayTra.Date))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.SoTienTra));

        var thongKe = allNames.Select(name =>
        {
            var kh = khachHangs.FirstOrDefault(k =>
                k.TenKhach.Equals(name, StringComparison.OrdinalIgnoreCase));
            var nameKey = name.ToUpperInvariant();

            var chiTiet = new Dictionary<DateTime, (decimal No, decimal Tra)>();
            foreach (var ngayCol in cacNgay)
            {
                gdByNameDate.TryGetValue((nameKey, ngayCol), out var no);
                tnByNameDate.TryGetValue((nameKey, ngayCol), out var tra);
                if (no != 0 || tra != 0)
                    chiTiet[ngayCol] = (no, tra);
            }

            return new ThongKeKhach
            {
                TenKhach = name,
                NoCu = kh?.NoCu ?? 0,
                TraNoCu = kh?.TraNoCu ?? 0,
                TongNoMoi = allGD.Where(g =>
                    g.TenKhach.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .Sum(g => g.ThanhTien),
                TongDaTra = allTN.Where(t =>
                    t.TenKhach.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .Sum(t => t.SoTienTra),
                ChiTietTheoNgay = chiTiet
            };
        }).Where(x => x.ConNo != 0 || x.TongNoMoi != 0 || x.TraNoCu != 0).ToList();

        var vm = new DashboardViewModel
        {
            NgayHienTai = today,
            NoMoiHomNay = noMoiHomNay,
            NoTraHomNay = noTraHomNay,
            ThongKeNoTheoKhach = thongKe,
            CacNgay = cacNgay,
            DanhSachKhach = khachHangs,
            DanhSachTenKhach = allNames,
            TongNo = thongKe.Sum(x => x.NoCu + x.TongNoMoi),
            TongTra = thongKe.Sum(x => x.TraNoCu + x.TongDaTra),
            TongConNo = thongKe.Sum(x => x.ConNo)
        };

        return View(vm);
    }

    // ===== GIAO DỊCH (NỢ MỚI) =====

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ThemGiaoDich(GiaoDich gd)
    {
        if (ModelState.IsValid)
        {
            if (gd.ThanhTien == 0 && gd.SoLuong > 0 && gd.Gia > 0)
                gd.ThanhTien = Math.Round(gd.SoLuong * gd.Gia);
            _db.GiaoDichs.Add(gd);
            await _db.SaveChangesAsync();

            // Auto-create KhachHang if not exists
            await EnsureKhachHang(gd.TenKhach);
        }
        return RedirectToAction(nameof(Index), new { ngay = gd.Ngay.ToString("yyyy-MM-dd") });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> XoaGiaoDich(int id)
    {
        var gd = await _db.GiaoDichs.FindAsync(id);
        if (gd != null)
        {
            var ngay = gd.Ngay;
            _db.GiaoDichs.Remove(gd);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index), new { ngay = ngay.ToString("yyyy-MM-dd") });
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SuaGiaoDich(GiaoDich gd)
    {
        var existing = await _db.GiaoDichs.FindAsync(gd.Id);
        if (existing != null)
        {
            existing.Ngay = gd.Ngay;
            existing.TenKhach = gd.TenKhach;
            existing.TenLai = gd.TenLai;
            existing.SoCon = gd.SoCon;
            existing.SoLuong = gd.SoLuong;
            existing.Gia = gd.Gia;
            existing.ThanhTien = gd.ThanhTien > 0 ? gd.ThanhTien : Math.Round(gd.SoLuong * gd.Gia);
            existing.TienTraLai = gd.TienTraLai;
            existing.GhiChu = gd.GhiChu;
            await _db.SaveChangesAsync();
            await EnsureKhachHang(gd.TenKhach);
        }
        return RedirectToAction(nameof(Index), new { ngay = gd.Ngay.ToString("yyyy-MM-dd") });
    }

    // ===== TRẢ NỢ =====

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ThemTraNo(TraNo traNo)
    {
        if (ModelState.IsValid)
        {
            _db.TraNos.Add(traNo);
            await _db.SaveChangesAsync();
            await EnsureKhachHang(traNo.TenKhach);
        }
        return RedirectToAction(nameof(Index), new { ngay = traNo.NgayTra.ToString("yyyy-MM-dd") });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> XoaTraNo(int id)
    {
        var traNo = await _db.TraNos.FindAsync(id);
        if (traNo != null)
        {
            _db.TraNos.Remove(traNo);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // ===== KHÁCH HÀNG =====

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ThemKhachHang(KhachHang kh)
    {
        if (ModelState.IsValid)
        {
            var existing = await _db.KhachHangs
                .FirstOrDefaultAsync(k => k.TenKhach == kh.TenKhach);
            if (existing != null)
            {
                existing.NoCu = kh.NoCu;
                existing.GhiChu = kh.GhiChu;
            }
            else
            {
                _db.KhachHangs.Add(kh);
            }
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> XoaKhachHang(int id)
    {
        var kh = await _db.KhachHangs.FindAsync(id);
        if (kh != null)
        {
            _db.KhachHangs.Remove(kh);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // ===== EXCEL IMPORT/EXPORT =====

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportExcel(IFormFile file, DateTime? ngayGiaoDich)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Vui lòng chọn file Excel.";
            return RedirectToAction(nameof(Index));
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".xls")
        {
            TempData["Error"] = "Chỉ hỗ trợ file .xlsx hoặc .xls";
            return RedirectToAction(nameof(Index));
        }

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position = 0;

        // Auto-detect file type
        var fileType = _excel.DetectFileType(stream);
        stream.Position = 0;

        if (fileType == "baocao")
        {
            // Import BÁO CÁO → KhachHang + aggregated GiaoDich + TraNo
            var (khachHangs, giaoDichs, traNos) = _excel.ImportBaoCao(stream);

            int addedKH = 0;
            foreach (var kh in khachHangs)
            {
                var existing = await _db.KhachHangs
                    .FirstOrDefaultAsync(k => k.TenKhach == kh.TenKhach);
                if (existing == null)
                {
                    _db.KhachHangs.Add(kh);
                    addedKH++;
                }
                else
                {
                    existing.NoCu = kh.NoCu;
                }
            }

            _db.GiaoDichs.AddRange(giaoDichs);
            _db.TraNos.AddRange(traNos);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Import BÁO CÁO: {addedKH} khách hàng mới, {giaoDichs.Count} khoản nợ, {traNos.Count} khoản trả.";
        }
        else
        {
            // Import daily transactions
            var ngay = ngayGiaoDich ?? DateTime.Today;
            var giaoDichs = _excel.ImportGiaoDichHangNgay(stream, ngay);

            if (giaoDichs.Count == 0)
            {
                TempData["Error"] = "Không đọc được dữ liệu từ file Excel.";
                return RedirectToAction(nameof(Index));
            }

            _db.GiaoDichs.AddRange(giaoDichs);

            // Auto-create KhachHang records
            var newNames = giaoDichs.Select(g => g.TenKhach).Distinct();
            foreach (var name in newNames)
                await EnsureKhachHang(name);

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã import {giaoDichs.Count} giao dịch ngày {ngay:dd/MM/yyyy}.";
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> ExportExcel(string loai = "all")
    {
        List<GiaoDich> data;
        string fileName;

        switch (loai)
        {
            case "today":
                data = await _db.GiaoDichs
                    .Where(g => g.Ngay.Date == DateTime.Today)
                    .ToListAsync();
                fileName = $"GiaoDich_{DateTime.Today:ddMMyyyy}.xlsx";
                break;
            default:
                data = await _db.GiaoDichs
                    .OrderBy(g => g.TenKhach).ThenBy(g => g.Ngay)
                    .ToListAsync();
                fileName = $"TatCa_GiaoDich_{DateTime.Today:ddMMyyyy}.xlsx";
                break;
        }

        var bytes = _excel.ExportGiaoDich(data);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public async Task<IActionResult> ExportBaoCao(DateTime? tuNgay, DateTime? denNgay)
    {
        var from = tuNgay ?? DateTime.Today.AddDays(-7);
        var to = denNgay ?? DateTime.Today;

        var khachHangs = await _db.KhachHangs.ToListAsync();
        var allGD = await _db.GiaoDichs
            .Where(g => g.Ngay.Date >= from.Date && g.Ngay.Date <= to.Date)
            .ToListAsync();
        var allTN = await _db.TraNos
            .Where(t => t.NgayTra.Date >= from.Date && t.NgayTra.Date <= to.Date)
            .ToListAsync();

        var allNames = khachHangs.Select(k => k.TenKhach)
            .Union(allGD.Select(g => g.TenKhach))
            .Union(allTN.Select(t => t.TenKhach))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var thongKe = allNames.Select(name =>
        {
            var kh = khachHangs.FirstOrDefault(k =>
                k.TenKhach.Equals(name, StringComparison.OrdinalIgnoreCase));
            return new ThongKeKhach
            {
                TenKhach = name,
                NoCu = kh?.NoCu ?? 0,
                TraNoCu = kh?.TraNoCu ?? 0,
                TongNoMoi = allGD.Where(g =>
                    g.TenKhach.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .Sum(g => g.ThanhTien),
                TongDaTra = allTN.Where(t =>
                    t.TenKhach.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .Sum(t => t.SoTienTra)
            };
        }).Where(x => x.ConNo != 0 || x.TongNoMoi != 0 || x.TraNoCu != 0).ToList();

        var bytes = _excel.ExportBaoCao(thongKe, allGD, allTN, from, to);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"BaoCao_CongNo_{to:ddMMyyyy}.xlsx");
    }

    // ===== API =====

    [HttpGet]
    public async Task<IActionResult> GetGiaoDich(int id)
    {
        var gd = await _db.GiaoDichs.FindAsync(id);
        if (gd == null) return NotFound();
        return Json(new
        {
            gd.Id,
            Ngay = gd.Ngay.ToString("yyyy-MM-dd"),
            gd.TenKhach,
            gd.TenLai,
            gd.SoCon,
            gd.SoLuong,
            gd.Gia,
            gd.ThanhTien,
            gd.TienTraLai,
            gd.SoLuongAnh,
            gd.NguonBanHang,
            gd.ImageOrder,
            gd.ImageImportRowId,
            gd.ReviewStatus,
            gd.GhiChu
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetDanhSachKhach()
    {
        var khachHangs = await _db.KhachHangs.OrderBy(k => k.TenKhach).ToListAsync();
        var fromGD = await _db.GiaoDichs.Select(g => g.TenKhach).Distinct().ToListAsync();
        var fromTN = await _db.TraNos.Select(t => t.TenKhach).Distinct().ToListAsync();

        var allNames = khachHangs.Select(k => k.TenKhach)
            .Union(fromGD)
            .Union(fromTN)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .Select(n => new { TenKhach = n })
            .ToList();

        return Json(allNames);
    }

    // ===== HELPERS =====

    private async Task EnsureKhachHang(string tenKhach)
    {
        tenKhach = CleanName(tenKhach);
        if (string.IsNullOrWhiteSpace(tenKhach)) return;
        var exists = await _db.KhachHangs
            .AnyAsync(k => k.TenKhach.ToUpper() == tenKhach.ToUpper());
        if (!exists)
        {
            _db.KhachHangs.Add(new KhachHang { TenKhach = tenKhach });
            await _db.SaveChangesAsync();
        }
    }

    private static DateTime ParseDateOrToday(string? dateText)
    {
        return DateTime.TryParse(dateText, out var date) ? date.Date : DateTime.Today;
    }

    private static string CleanName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static object ToImageBatchDto(ImageImportBatch batch)
    {
        return new
        {
            batch.Id,
            Ngay = batch.Ngay.ToString("yyyy-MM-dd"),
            batch.LoaiImport,
            batch.NguonBanHang,
            batch.OriginalFileName,
            batch.StoredFileName,
            batch.ImagePath,
            batch.Status,
            batch.RawText,
            batch.ParseError,
            batch.CreatedAt,
            batch.UpdatedAt,
            Rows = batch.Rows
                .OrderBy(r => r.ImageOrder)
                .ThenBy(r => r.Id)
                .Select(r => new
                {
                    r.Id,
                    r.ImageImportBatchId,
                    r.ImageOrder,
                    r.TenLai,
                    r.TenKhach,
                    r.SoLuongAnh,
                    r.SoTienTra,
                    r.Confidence,
                    r.RawLine,
                    r.MatchedGiaoDichId,
                    r.MatchedTraNoId,
                    r.ReviewStatus,
                    r.Notes,
                    r.CreatedAt,
                    r.UpdatedAt,
                    MatchedGiaoDich = r.MatchedGiaoDich == null ? null : new
                    {
                        r.MatchedGiaoDich.Id,
                        r.MatchedGiaoDich.TenLai,
                        r.MatchedGiaoDich.TenKhach,
                        r.MatchedGiaoDich.SoCon,
                        SoLuongExcel = r.MatchedGiaoDich.SoLuong,
                        r.MatchedGiaoDich.SoLuongAnh,
                        r.MatchedGiaoDich.Gia,
                        r.MatchedGiaoDich.ThanhTien,
                        r.MatchedGiaoDich.TienTraLai,
                        r.MatchedGiaoDich.ReviewStatus
                    },
                    MatchedTraNo = r.MatchedTraNo == null ? null : new
                    {
                        r.MatchedTraNo.Id,
                        r.MatchedTraNo.TenKhach,
                        r.MatchedTraNo.TenLai,
                        r.MatchedTraNo.SoTienTra,
                        r.MatchedTraNo.ReviewStatus
                    }
                })
        };
    }

    private static object ToImageRowDto(ImageImportBatch batch, ImageImportRow row)
    {
        return new
        {
            row.Id,
            BatchId = batch.Id,
            Ngay = batch.Ngay.ToString("yyyy-MM-dd"),
            batch.LoaiImport,
            batch.NguonBanHang,
            batch.ImagePath,
            row.ImageOrder,
            row.TenLai,
            row.TenKhach,
            row.SoLuongAnh,
            row.SoTienTra,
            row.Confidence,
            row.RawLine,
            row.MatchedGiaoDichId,
            row.MatchedTraNoId,
            row.ReviewStatus,
            Status = row.ReviewStatus,
            row.Notes,
            MatchedGiaoDich = row.MatchedGiaoDich == null ? null : new
            {
                row.MatchedGiaoDich.Id,
                row.MatchedGiaoDich.TenLai,
                row.MatchedGiaoDich.TenKhach,
                row.MatchedGiaoDich.SoCon,
                SoLuongExcel = row.MatchedGiaoDich.SoLuong,
                row.MatchedGiaoDich.SoLuongAnh,
                row.MatchedGiaoDich.Gia,
                row.MatchedGiaoDich.ThanhTien,
                row.MatchedGiaoDich.TienTraLai,
                row.MatchedGiaoDich.ReviewStatus
            },
            MatchedTraNo = row.MatchedTraNo == null ? null : new
            {
                row.MatchedTraNo.Id,
                row.MatchedTraNo.TenKhach,
                row.MatchedTraNo.TenLai,
                row.MatchedTraNo.SoTienTra,
                row.MatchedTraNo.ReviewStatus
            }
        };
    }

    private static ImageImportReviewRequest ParseImageImportReviewRequest(JsonElement payload)
    {
        var request = new ImageImportReviewRequest();
        var rowsElement = payload;

        if (payload.ValueKind == JsonValueKind.Object &&
            TryGetJsonProperty(payload, "rows", out var nestedRows))
        {
            rowsElement = nestedRows;
        }

        if (rowsElement.ValueKind != JsonValueKind.Array)
            return request;

        foreach (var item in rowsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var rowId = GetJsonInt(item, "rowId") ?? GetJsonInt(item, "id") ?? 0;
            if (rowId <= 0)
                continue;

            request.Rows.Add(new ImageImportReviewRowRequest
            {
                RowId = rowId,
                MatchedGiaoDichId = GetJsonInt(item, "matchedGiaoDichId"),
                MatchedTraNoId = GetJsonInt(item, "matchedTraNoId"),
                TenLai = GetJsonString(item, "tenLai"),
                TenKhach = GetJsonString(item, "tenKhach"),
                SoLuongAnh = GetJsonDecimal(item, "soLuongAnh"),
                SoTienTra = GetJsonDecimal(item, "soTienTra"),
                Notes = GetJsonString(item, "notes"),
                Confirmed = GetJsonBool(item, "confirmed") ?? true
            });
        }

        return request;
    }

    private static bool TryGetJsonProperty(JsonElement item, string name, out JsonElement value)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetJsonString(JsonElement item, string name)
    {
        return TryGetJsonProperty(item, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetJsonInt(JsonElement item, string name)
    {
        if (!TryGetJsonProperty(item, name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
            return number;

        return null;
    }

    private static decimal? GetJsonDecimal(JsonElement item, string name)
    {
        if (!TryGetJsonProperty(item, name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out number))
            return number;

        return null;
    }

    private static bool? GetJsonBool(JsonElement item, string name)
    {
        if (!TryGetJsonProperty(item, name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result))
            return result;

        return null;
    }

    // ===== UPLOAD ẢNH =====

    [HttpPost]
    public async Task<IActionResult> UploadImages(List<IFormFile> files, string? ngay,
        string? loaiImport, string? nguonBanHang)
    {
        if (files == null || files.Count == 0)
        {
            TempData["Error"] = "Chưa chọn file ảnh!";
            return RedirectToAction("Index", new { ngay });
        }

        var date = ParseDateOrToday(ngay);
        var batches = await _imageImports.CreateBatchesAsync(files, date, loaiImport, nguonBanHang);
        if (batches.Count == 0)
        {
            TempData["Error"] = "Không có file ảnh hợp lệ để import.";
            return RedirectToAction("Index", new { ngay });
        }

        var parsedRows = batches.Sum(b => b.Rows.Count);
        TempData["Success"] = $"Đã upload {batches.Count} ảnh, tạo {parsedRows} dòng review.";
        return RedirectToAction("Index", new { ngay, tab = "hoan-thien" });
    }

    [HttpPost]
    public async Task<IActionResult> ImportImageBatch(List<IFormFile> files, string? ngay,
        string? loaiImport, string? nguonBanHang)
    {
        if (files == null || files.Count == 0)
            return BadRequest("Chưa chọn file ảnh.");

        var date = ParseDateOrToday(ngay);
        var batches = await _imageImports.CreateBatchesAsync(files, date, loaiImport, nguonBanHang);
        return Json(new
        {
            success = true,
            count = batches.Count,
            rows = batches.Sum(b => b.Rows.Count),
            batches = batches.Select(ToImageBatchDto)
        });
    }

    [HttpGet]
    public IActionResult GetUploadedImages()
    {
        var uploadsDir = GetUploadsRoot();
        if (!Directory.Exists(uploadsDir))
            return Json(new List<string>());

        var images = Directory.GetFiles(uploadsDir, "*.*", SearchOption.AllDirectories)
            .Where(f => new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" }
                .Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => "/uploads/" + Path.GetRelativePath(uploadsDir, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderByDescending(f => f)
            .ToList();

        return Json(images);
    }

    [HttpGet]
    public async Task<IActionResult> GetImageImportBatches(string? ngay, string? loaiImport, string? nguonBanHang)
    {
        var date = string.IsNullOrWhiteSpace(ngay) ? (DateTime?)null : ParseDateOrToday(ngay);
        var batches = await _imageImports.GetBatchesAsync(date, loaiImport, nguonBanHang);
        return Json(batches.Select(ToImageBatchDto));
    }

    [HttpGet]
    public async Task<IActionResult> GetImageImportRows(string? ngay, string? loaiImport, string? nguonBanHang)
    {
        var date = string.IsNullOrWhiteSpace(ngay) ? (DateTime?)null : ParseDateOrToday(ngay);
        var batches = await _imageImports.GetBatchesAsync(date, loaiImport, nguonBanHang);
        var rows = batches
            .OrderBy(b => b.CreatedAt)
            .ThenBy(b => b.Id)
            .SelectMany(batch => batch.Rows
                .OrderBy(r => r.ImageOrder)
                .ThenBy(r => r.Id)
                .Select(row => ToImageRowDto(batch, row)))
            .ToList();

        return Json(rows);
    }

    [HttpPost]
    public IActionResult XoaImage(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return BadRequest();
        var safeName = Path.GetFileName(fileName); // prevent path traversal
        var uploadsDir = GetUploadsRoot();
        var filePath = Directory.Exists(uploadsDir)
            ? Directory.GetFiles(uploadsDir, safeName, SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (System.IO.File.Exists(filePath))
            System.IO.File.Delete(filePath);
        return Ok();
    }

    private string GetUploadsRoot()
    {
        var configured = _configuration["Uploads:RootPath"] ?? Environment.GetEnvironmentVariable("UPLOADS_ROOT");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(_env.WebRootPath, "uploads")
            : configured;
    }

    [HttpPost]
    public async Task<IActionResult> ApplyImageImportReview([FromBody] JsonElement payload)
    {
        var request = ParseImageImportReviewRequest(payload);
        if (request.Rows.Count == 0)
            return BadRequest("Không có dòng review để lưu.");

        var result = await _imageImports.ApplyReviewAsync(request);
        return Json(new
        {
            success = true,
            updated = result.Applied,
            result.Applied,
            result.Pending,
            result.Skipped
        });
    }

    // ===== HOÀN THIỆN DỮ LIỆU (cập nhật TenKhach từ ảnh) =====

    /// <summary>
    /// Lấy danh sách giao dịch chưa có tên khách mua, nhóm theo lái
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetIncompleteRecords(string? ngay)
    {
        DateTime date = string.IsNullOrEmpty(ngay) ? DateTime.Today : DateTime.Parse(ngay);

        var records = await _db.GiaoDichs
            .Where(g => g.Ngay.Date == date.Date && (g.TenKhach == null || g.TenKhach == ""))
            .OrderBy(g => g.TenLai)
            .ThenBy(g => g.Id)
            .Select(g => new
            {
                g.Id,
                g.TenLai,
                g.TenKhach,
                g.SoCon,
                g.SoLuong,
                g.SoLuongAnh,
                g.Gia,
                g.ThanhTien,
                g.TienTraLai,
                g.NguonBanHang,
                g.ImageOrder,
                g.ImageImportRowId,
                g.ReviewStatus
            })
            .ToListAsync();

        return Json(records);
    }

    /// <summary>
    /// Batch update tên khách mua cho các giao dịch từ dữ liệu ảnh
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CapNhatTenKhach([FromBody] List<CapNhatKhachRequest> items)
    {
        if (items == null || items.Count == 0)
            return BadRequest("Không có dữ liệu!");

        int updated = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.TenKhach)) continue;

            var gd = await _db.GiaoDichs.FindAsync(item.Id);
            if (gd != null)
            {
                gd.TenKhach = CleanName(item.TenKhach);
                await EnsureKhachHang(gd.TenKhach);
                updated++;
            }
        }

        await _db.SaveChangesAsync();
        return Json(new { success = true, updated });
    }

    [HttpPost]
    public async Task<IActionResult> CapNhatBangTraNo([FromBody] List<CapNhatTraNoRequest> items)
    {
        if (items == null || items.Count == 0)
            return BadRequest("Không có dữ liệu!");

        var changed = 0;
        foreach (var item in items)
        {
            var tenKhach = CleanName(item.TenKhach);
            if (string.IsNullOrWhiteSpace(tenKhach)) continue;
            if (item.SoTienTra < 0) return BadRequest("Số tiền trả không được âm.");

            var ngayTra = item.NgayTra.Date;
            var existing = await _db.TraNos
                .Where(t => t.TenKhach == tenKhach && t.NgayTra.Date == ngayTra)
                .ToListAsync();

            if (existing.Any())
                _db.TraNos.RemoveRange(existing);

            if (item.SoTienTra > 0)
            {
                _db.TraNos.Add(new TraNo
                {
                    TenKhach = tenKhach,
                    NgayTra = ngayTra,
                    SoTienTra = item.SoTienTra,
                    GhiChu = "Nhập từ bảng trả nợ"
                });
                await EnsureKhachHang(tenKhach);
            }

            changed++;
        }

        await _db.SaveChangesAsync();
        return Json(new { success = true, updated = changed });
    }

    [HttpPost]
    public async Task<IActionResult> CapNhatBangTraNoCu([FromBody] List<CapNhatTraNoCuRequest> items)
    {
        if (items == null || items.Count == 0)
            return BadRequest("Không có dữ liệu!");

        var changed = 0;
        foreach (var item in items)
        {
            var tenKhach = CleanName(item.TenKhach);
            if (string.IsNullOrWhiteSpace(tenKhach)) continue;

            var khachHang = await _db.KhachHangs
                .FirstOrDefaultAsync(k => k.TenKhach.ToUpper() == tenKhach.ToUpper());

            if (khachHang == null)
            {
                _db.KhachHangs.Add(new KhachHang
                {
                    TenKhach = tenKhach,
                    TraNoCu = item.TraNoCu
                });
            }
            else
            {
                khachHang.TraNoCu = item.TraNoCu;
            }

            changed++;
        }

        await _db.SaveChangesAsync();
        return Json(new { success = true, updated = changed });
    }
}

public class CapNhatKhachRequest
{
    public int Id { get; set; }
    public string TenKhach { get; set; } = "";
}

public class CapNhatTraNoRequest
{
    public string TenKhach { get; set; } = "";
    public DateTime NgayTra { get; set; }
    public decimal SoTienTra { get; set; }
}

public class CapNhatTraNoCuRequest
{
    public string TenKhach { get; set; } = "";
    public decimal TraNoCu { get; set; }
}
