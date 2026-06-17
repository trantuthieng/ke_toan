using ClosedXML.Excel;
using ExcelDataReader;
using System.Globalization;
using QuanLyNo.Models;
using QuanLyNo.ViewModels;

namespace QuanLyNo.Services;

public class ExcelService
{
    // ── HELPERS ──────────────────────────────────────────────────

    // Reads only the FIRST sheet, row by row, into a plain list of arrays.
    // Does NOT call AsDataSet() — that would load ALL sheets into memory.
    private static List<object?[]> ReadRows(Stream stream)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var rows = new List<object?[]>();
        while (reader.Read()) // iterate rows of first sheet only
        {
            var row = new object?[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        // NOT calling reader.NextResult() → subsequent sheets are never loaded
        return rows;
    }

    // col is 1-based (Excel convention)
    private static string GetStr(object?[] row, int col)
    {
        int i = col - 1;
        return i < row.Length ? row[i]?.ToString()?.Trim() ?? "" : "";
    }

    private static decimal GetDec(object?[] row, int col)
    {
        int i = col - 1;
        if (i >= row.Length) return 0;
        var v = row[i];
        if (v == null) return 0;
        if (v is double d) return (decimal)d;
        if (v is decimal dec) return dec;
        if (v is int ii) return ii;
        if (v is long l) return l;
        return decimal.TryParse(v.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var r) ? r : 0;
    }

    // ── DETECT ───────────────────────────────────────────────────

    private static string DetectFileType(List<object?[]> rows)
    {
        var r1 = rows.Count > 0 ? rows[0][0]?.ToString() ?? "" : "";
        if (r1.Contains("BÁO CÁO", StringComparison.OrdinalIgnoreCase) ||
            r1.Contains("CÔNG NỢ", StringComparison.OrdinalIgnoreCase))
            return "baocao";

        if (rows.Count > 2)
        {
            int nonEmpty = rows[2].Count(v => v != null && !string.IsNullOrWhiteSpace(v.ToString()));
            if (nonEmpty >= 10) return "baocao";
        }

        return "hangngay";
    }

    // ── PUBLIC IMPORT ────────────────────────────────────────────

    public (string fileType,
            List<GiaoDich> giaoDichs,
            List<KhachHang> khachHangs,
            List<TraNo> traNos) DetectAndImport(Stream stream, DateTime ngay)
    {
        var rows = ReadRows(stream);
        var fileType = DetectFileType(rows);

        if (fileType == "baocao")
        {
            var (khs, gds, tns) = ImportBaoCao(rows);
            return (fileType, gds, khs, tns);
        }
        else
        {
            var gds = ImportGiaoDich(rows, ngay);
            return (fileType, gds, new List<KhachHang>(), new List<TraNo>());
        }
    }

    public List<GiaoDich> ImportGiaoDichHangNgay(Stream stream, DateTime ngay)
    {
        return ImportGiaoDich(ReadRows(stream), ngay);
    }

    public (List<KhachHang> khachHangs, List<GiaoDich> giaoDichs, List<TraNo> traNos) ImportBaoCao(Stream stream)
    {
        return ImportBaoCao(ReadRows(stream));
    }

    // ── PRIVATE IMPORT ───────────────────────────────────────────

    private List<GiaoDich> ImportGiaoDich(List<object?[]> rows, DateTime ngay)
    {
        if (IsKhachHangLaiFormat(rows))
            return ImportKhachHangLaiFormat(rows, ngay);

        var allItems = new List<GiaoDich>();
        string currentLai = "";
        var group = new List<GiaoDich>();

        for (int r = 2; r < rows.Count; r++) // data from row 3 (index 2)
        {
            var row = rows[r];

            if (GetDec(row, 2) == 0 && GetDec(row, 3) == 0 && GetDec(row, 5) == 0
                && string.IsNullOrWhiteSpace(GetStr(row, 1)))
            {
                FlushGroup(currentLai, group, allItems);
                group.Clear();
                continue;
            }

            string kh = GetStr(row, 1);
            if (!string.IsNullOrEmpty(kh) && kh != currentLai)
            {
                FlushGroup(currentLai, group, allItems);
                group.Clear();
                currentLai = kh;
            }

            if (string.IsNullOrEmpty(currentLai)) continue;

            decimal sl = GetDec(row, 3);
            if (sl > 0)
            {
                group.Add(new GiaoDich
                {
                    Ngay = ngay,
                    TenLai = currentLai,
                    TenKhach = "",
                    SoCon = GetDec(row, 2),
                    SoLuong = sl,
                    Gia = GetDec(row, 4),
                    ThanhTien = GetDec(row, 5),
                    TienTraLai = GetDec(row, 6)
                });
            }
        }

        FlushGroup(currentLai, group, allItems);
        return allItems;
    }

    private bool IsKhachHangLaiFormat(List<object?[]> rows)
    {
        int limit = Math.Min(rows.Count, 82);
        int dataRows = 0, namedDataRows = 0;

        for (int r = 2; r < limit; r++)
        {
            var row = rows[r];
            if (GetDec(row, 5) == 0) continue;
            dataRows++;
            if (!string.IsNullOrWhiteSpace(GetStr(row, 1))) namedDataRows++;
        }

        return dataRows >= 5 && namedDataRows >= dataRows * 0.8m;
    }

    private bool FirstDataRowIsSeller(List<object?[]> rows)
    {
        for (int r = 2; r < rows.Count; r++)
        {
            var row = rows[r];
            if (string.IsNullOrWhiteSpace(GetStr(row, 1))) continue;
            return GetDec(row, 5) == 0 && GetDec(row, 3) == 0 && GetDec(row, 4) == 0;
        }
        return false;
    }

    private List<GiaoDich> ImportKhachHangLaiFormat(List<object?[]> rows, DateTime ngay)
    {
        var result = new List<GiaoDich>();
        var pending = new List<GiaoDich>();
        var currentLai = "";
        var sellerFirst = FirstDataRowIsSeller(rows);

        for (int r = 2; r < rows.Count; r++)
        {
            var row = rows[r];
            var ten = GetStr(row, 1);
            if (string.IsNullOrWhiteSpace(ten)) continue;

            decimal thanhTien = GetDec(row, 5);
            decimal soLuong = GetDec(row, 3);
            decimal gia = GetDec(row, 4);

            if (thanhTien == 0 && soLuong == 0 && gia == 0)
            {
                if (sellerFirst)
                {
                    currentLai = ten;
                }
                else
                {
                    foreach (var gd in pending) gd.TenLai = ten;
                    result.AddRange(pending);
                    pending.Clear();
                }
                continue;
            }

            if (thanhTien == 0 || soLuong == 0) continue;

            var item = new GiaoDich
            {
                Ngay = ngay,
                TenLai = sellerFirst ? currentLai : "",
                TenKhach = ten,
                SoCon = GetDec(row, 2),
                SoLuong = soLuong,
                Gia = gia,
                ThanhTien = thanhTien,
                TienTraLai = GetDec(row, 6)
            };

            if (sellerFirst)
                result.Add(item);
            else
                pending.Add(item);
        }

        if (pending.Count > 0)
        {
            foreach (var gd in pending) gd.TenLai = currentLai;
            result.AddRange(pending);
        }

        return result;
    }

    private (List<KhachHang> khachHangs, List<GiaoDich> giaoDichs, List<TraNo> traNos) ImportBaoCao(List<object?[]> rows)
    {
        var khachHangs = new List<KhachHang>();
        var giaoDichs = new List<GiaoDich>();
        var traNos = new List<TraNo>();

        var dates = new List<DateTime>();
        if (rows.Count > 2)
        {
            var row3 = rows[2];
            for (int c = 4; c < row3.Length; c += 2) // 1-based col 4 = index 3
            {
                var cell = row3[c - 1]; // 0-based index
                if (cell == null) continue;
                if (cell is DateTime dt2) { dates.Add(dt2.Date); continue; }
                if (cell is double d) { dates.Add(DateTime.FromOADate(d).Date); continue; }
                if (DateTime.TryParse(cell.ToString(), out var parsed)) dates.Add(parsed.Date);
            }
        }

        for (int r = 4; r < rows.Count; r++) // data from row 5 (index 4)
        {
            var row = rows[r];
            string tenKhach = GetStr(row, 1);
            if (string.IsNullOrEmpty(tenKhach)) continue;
            if (tenKhach.StartsWith("TỔNG", StringComparison.OrdinalIgnoreCase) ||
                tenKhach.StartsWith("Cộng", StringComparison.OrdinalIgnoreCase)) continue;

            decimal noCu = GetDec(row, 2);
            decimal noCu2 = GetDec(row, 3);
            khachHangs.Add(new KhachHang { TenKhach = tenKhach, NoCu = noCu != 0 ? noCu : noCu2 });

            for (int d = 0; d < dates.Count; d++)
            {
                decimal noAmount = GetDec(row, 4 + d * 2);
                decimal traAmount = GetDec(row, 4 + d * 2 + 1);

                if (noAmount != 0)
                    giaoDichs.Add(new GiaoDich { Ngay = dates[d], TenKhach = tenKhach, ThanhTien = noAmount, GhiChu = "Import từ báo cáo" });
                if (traAmount != 0)
                    traNos.Add(new TraNo { NgayTra = dates[d], TenKhach = tenKhach, SoTienTra = traAmount, GhiChu = "Import từ báo cáo" });
            }
        }

        return (khachHangs, giaoDichs, traNos);
    }

    private void FlushGroup(string lai, List<GiaoDich> items, List<GiaoDich> result)
    {
        if (string.IsNullOrEmpty(lai) || items.Count == 0) return;

        if (items.Count > 1)
        {
            var last = items[^1];
            var sumSC = items.Take(items.Count - 1).Sum(x => x.SoCon);
            if (Math.Abs(last.SoCon - sumSC) < 0.5m)
            {
                result.AddRange(items.Take(items.Count - 1));
                return;
            }
        }

        result.AddRange(items);
    }

    /// <summary>
    /// Export danh sách giao dịch ra file Excel
    /// </summary>
    public byte[] ExportGiaoDich(List<GiaoDich> danhSach, string tieuDe = "Giao dịch")
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(tieuDe.Length > 31 ? tieuDe[..31] : tieuDe);

        string[] headers = { "Ngày", "Khách hàng", "SC", "SL (kg)", "Giá", "Thành tiền", "Tiền trả lái", "Ghi chú" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        var hdr = ws.Range(1, 1, 1, headers.Length);
        hdr.Style.Font.Bold = true;
        hdr.Style.Fill.BackgroundColor = XLColor.LightBlue;

        int row = 2;
        foreach (var gd in danhSach.OrderBy(g => g.TenKhach).ThenBy(g => g.Ngay))
        {
            ws.Cell(row, 1).Value = gd.Ngay;
            ws.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Cell(row, 2).Value = gd.TenKhach;
            ws.Cell(row, 3).Value = gd.SoCon;
            ws.Cell(row, 4).Value = gd.SoLuong;
            ws.Cell(row, 5).Value = gd.Gia;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 6).Value = gd.ThanhTien;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 7).Value = gd.TienTraLai;
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 8).Value = gd.GhiChu ?? "";
            row++;
        }

        // Tổng cộng
        ws.Cell(row, 5).Value = "TỔNG:";
        ws.Cell(row, 5).Style.Font.Bold = true;
        ws.Cell(row, 6).FormulaA1 = $"SUM(F2:F{row - 1})";
        ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
        ws.Cell(row, 6).Style.Font.Bold = true;

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Export BÁO CÁO tổng hợp công nợ (format giống file mẫu)
    /// </summary>
    public byte[] ExportBaoCao(List<ThongKeKhach> thongKe,
        List<GiaoDich> giaoDichs, List<TraNo> traNos,
        DateTime tuNgay, DateTime denNgay)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("BÁO CÁO");

        // Date range
        var dates = new List<DateTime>();
        for (var d = tuNgay.Date; d <= denNgay.Date; d = d.AddDays(1))
            dates.Add(d);

        // Row 1: Title
        ws.Cell(1, 1).Value = $"BÁO CÁO TỔNG HỢP CÔNG NỢ NGÀY {denNgay:dd/MM/yyyy}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        // Row 3-4: Headers
        ws.Cell(3, 1).Value = "Khách";
        ws.Cell(3, 2).Value = "Nợ cũ";

        for (int i = 0; i < dates.Count; i++)
        {
            int col = 3 + i * 2;
            ws.Cell(3, col).Value = dates[i];
            ws.Cell(3, col).Style.DateFormat.Format = "d/M";
            ws.Cell(4, col).Value = "Nợ";
            ws.Cell(4, col + 1).Value = "Trả";
        }

        int tongNoCol = 3 + dates.Count * 2;
        ws.Cell(3, tongNoCol).Value = "Tổng nợ";

        // Style headers
        var hdrRange = ws.Range(3, 1, 4, tongNoCol);
        hdrRange.Style.Font.Bold = true;
        hdrRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
        hdrRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        hdrRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        hdrRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        // Data rows
        int row = 5;
        foreach (var tk in thongKe.OrderBy(x => x.TenKhach))
        {
            ws.Cell(row, 1).Value = tk.TenKhach;
            ws.Cell(row, 2).Value = tk.NoCu;
            ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0";

            for (int i = 0; i < dates.Count; i++)
            {
                int col = 3 + i * 2;
                var ngay = dates[i].Date;

                decimal noNgay = giaoDichs
                    .Where(g => g.TenKhach == tk.TenKhach && g.Ngay.Date == ngay)
                    .Sum(g => g.ThanhTien);
                decimal traNgay = traNos
                    .Where(t => t.TenKhach == tk.TenKhach && t.NgayTra.Date == ngay)
                    .Sum(t => t.SoTienTra);

                if (noNgay != 0)
                {
                    ws.Cell(row, col).Value = noNgay;
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0";
                }
                if (traNgay != 0)
                {
                    ws.Cell(row, col + 1).Value = traNgay;
                    ws.Cell(row, col + 1).Style.NumberFormat.Format = "#,##0";
                }
            }

            ws.Cell(row, tongNoCol).Value = tk.ConNo;
            ws.Cell(row, tongNoCol).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, tongNoCol).Style.Font.Bold = true;

            // Highlight negative debt (customer has credit)
            if (tk.ConNo < 0)
                ws.Cell(row, tongNoCol).Style.Font.FontColor = XLColor.Green;
            else if (tk.ConNo > 0)
                ws.Cell(row, tongNoCol).Style.Font.FontColor = XLColor.Red;

            row++;
        }

        // Total row
        ws.Cell(row, 1).Value = "TỔNG CỘNG";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 2).Value = thongKe.Sum(x => x.NoCu);
        ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0";
        ws.Cell(row, 2).Style.Font.Bold = true;
        ws.Cell(row, tongNoCol).Value = thongKe.Sum(x => x.ConNo);
        ws.Cell(row, tongNoCol).Style.NumberFormat.Format = "#,##0";
        ws.Cell(row, tongNoCol).Style.Font.Bold = true;
        ws.Cell(row, tongNoCol).Style.Font.FontColor = XLColor.Red;

        // Borders for data area
        if (row > 5)
        {
            var dataRange = ws.Range(5, 1, row, tongNoCol);
            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

}
