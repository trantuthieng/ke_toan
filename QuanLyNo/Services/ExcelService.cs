using ClosedXML.Excel;
using QuanLyNo.Models;
using QuanLyNo.ViewModels;

namespace QuanLyNo.Services;

public class ExcelService
{
    /// <summary>
    /// Import giao dịch hàng ngày từ file Excel (format: KH | SC | SL | GIÁ | T.TIỀN | TiềnLái)
    /// Nhóm theo KH (lái/khách bán), tự động bỏ dòng subtotal
    /// KH trong Excel = TenLai (khách hàng bán). TenKhach (khách mua) sẽ được bổ sung từ ảnh.
    /// </summary>
    public List<GiaoDich> ImportGiaoDichHangNgay(Stream stream, DateTime ngay)
    {
        var allItems = new List<GiaoDich>();
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();

        string currentLai = "";
        var group = new List<GiaoDich>();
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        for (int r = 3; r <= lastRow; r++)
        {
            var row = ws.Row(r);

            // Skip fully empty rows → flush current group
            if (row.Cell(2).IsEmpty() && row.Cell(3).IsEmpty() && row.Cell(5).IsEmpty())
            {
                FlushGroup(currentLai, group, allItems);
                group.Clear();
                continue;
            }

            // Check for new lái name in col 1
            string kh = row.Cell(1).IsEmpty() ? "" : row.Cell(1).GetString().Trim();
            if (!string.IsNullOrEmpty(kh) && kh != currentLai)
            {
                FlushGroup(currentLai, group, allItems);
                group.Clear();
                currentLai = kh;
            }

            if (string.IsNullOrEmpty(currentLai)) continue;

            decimal sc = GetDecimal(row.Cell(2));
            decimal sl = GetDecimal(row.Cell(3));
            decimal gia = GetDecimal(row.Cell(4));
            decimal ttien = GetDecimal(row.Cell(5));
            decimal tienLai = GetDecimal(row.Cell(6));

            if (sl > 0)
            {
                group.Add(new GiaoDich
                {
                    Ngay = ngay,
                    TenLai = currentLai,
                    TenKhach = "",  // sẽ bổ sung từ ảnh
                    SoCon = sc,
                    SoLuong = sl,
                    Gia = gia,
                    ThanhTien = ttien,
                    TienTraLai = tienLai
                });
            }
        }

        FlushGroup(currentLai, group, allItems);
        return allItems;
    }

    private void FlushGroup(string lai, List<GiaoDich> items, List<GiaoDich> result)
    {
        if (string.IsNullOrEmpty(lai) || items.Count == 0) return;

        if (items.Count > 1)
        {
            // Detect subtotal: last row's SC ≈ sum of all previous rows' SC
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
    /// Import BÁO CÁO tổng hợp công nợ → extract KhachHang, GiaoDich aggregates, TraNo
    /// Format: Khách | Nợ cũ | (Nợ cũ2) | [per-day Nợ|Trả pairs] | Tổng nợ
    /// </summary>
    public (List<KhachHang> khachHangs, List<GiaoDich> giaoDichs, List<TraNo> traNos) ImportBaoCao(Stream stream)
    {
        var khachHangs = new List<KhachHang>();
        var giaoDichs = new List<GiaoDich>();
        var traNos = new List<TraNo>();

        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();

        // Parse date columns from row 3 headers
        // Col1=Khách, Col2=Nợ cũ, Col3=(Nợ cũ merged), then pairs of (Nợ|Trả) per date, last col=Tổng nợ
        var dates = new List<DateTime>();
        int lastCol = ws.Row(3).LastCellUsed()?.Address.ColumnNumber ?? 0;

        // Date columns start from col 4, in pairs
        for (int c = 4; c < lastCol; c += 2)
        {
            var cell = ws.Cell(3, c);
            if (!cell.IsEmpty())
            {
                try
                {
                    dates.Add(cell.GetDateTime().Date);
                }
                catch
                {
                    // Try parsing as text
                    var text = cell.GetString().Trim();
                    if (DateTime.TryParse(text, out var dt))
                        dates.Add(dt.Date);
                }
            }
        }

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        for (int r = 5; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            string tenKhach = row.Cell(1).IsEmpty() ? "" : row.Cell(1).GetString().Trim();
            if (string.IsNullOrEmpty(tenKhach)) continue;

            // Skip total/summary rows
            if (tenKhach.StartsWith("TỔNG", StringComparison.OrdinalIgnoreCase) ||
                tenKhach.StartsWith("Cộng", StringComparison.OrdinalIgnoreCase))
                continue;

            decimal noCu = GetDecimal(row.Cell(2));
            // Col3 might also have data (second nợ cũ column or adjustment)
            decimal noCu2 = GetDecimal(row.Cell(3));
            decimal noCuFinal = noCu != 0 ? noCu : noCu2;

            khachHangs.Add(new KhachHang { TenKhach = tenKhach, NoCu = noCuFinal });

            // Parse daily Nợ/Trả amounts
            for (int d = 0; d < dates.Count; d++)
            {
                int colNo = 4 + d * 2;
                int colTra = 4 + d * 2 + 1;

                decimal noAmount = GetDecimal(row.Cell(colNo));
                decimal traAmount = GetDecimal(row.Cell(colTra));

                if (noAmount != 0)
                {
                    giaoDichs.Add(new GiaoDich
                    {
                        Ngay = dates[d],
                        TenKhach = tenKhach,
                        ThanhTien = noAmount,
                        GhiChu = "Import từ báo cáo"
                    });
                }

                if (traAmount != 0)
                {
                    traNos.Add(new TraNo
                    {
                        NgayTra = dates[d],
                        TenKhach = tenKhach,
                        SoTienTra = traAmount,
                        GhiChu = "Import từ báo cáo"
                    });
                }
            }
        }

        return (khachHangs, giaoDichs, traNos);
    }

    /// <summary>
    /// Tự động phát hiện loại file: "baocao" hoặc "hangngay"
    /// </summary>
    public string DetectFileType(Stream stream)
    {
        stream.Position = 0;
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();

        // Check row 1 for "BÁO CÁO" keyword
        var r1 = ws.Cell(1, 1).IsEmpty() ? "" : ws.Cell(1, 1).GetString();
        if (r1.Contains("BÁO CÁO", StringComparison.OrdinalIgnoreCase) ||
            r1.Contains("CÔNG NỢ", StringComparison.OrdinalIgnoreCase))
            return "baocao";

        // Check if row 3 has many columns (report has 10+ columns)
        int cols = ws.Row(3).LastCellUsed()?.Address.ColumnNumber ?? 0;
        if (cols >= 10) return "baocao";

        return "hangngay";
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

    private static decimal GetDecimal(IXLCell cell)
    {
        if (cell.IsEmpty()) return 0;
        try { return (decimal)cell.GetDouble(); }
        catch { return 0; }
    }
}
