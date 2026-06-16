namespace QuanLyNo.ViewModels;

using QuanLyNo.Models;

public class DashboardViewModel
{
    public DateTime NgayHienTai { get; set; } = DateTime.Today;

    // Tab 1: Nợ mới hôm nay (giao dịch mua nợ)
    public List<GiaoDich> NoMoiHomNay { get; set; } = new();

    // Tab 2: Nợ được trả hôm nay
    public List<TraNo> NoTraHomNay { get; set; } = new();

    // Tab 3: Báo cáo công nợ dạng bảng (giống file BÁO CÁO)
    public List<ThongKeKhach> ThongKeNoTheoKhach { get; set; } = new();
    public List<DateTime> CacNgay { get; set; } = new(); // danh sách ngày có dữ liệu (cột)

    // Danh sách khách hàng (cho dropdown)
    public List<KhachHang> DanhSachKhach { get; set; } = new();
    public List<string> DanhSachTenKhach { get; set; } = new();

    // Báo cáo
    public decimal TongNo { get; set; }
    public decimal TongTra { get; set; }
    public decimal TongConNo { get; set; }
}

public class ThongKeKhach
{
    public string TenKhach { get; set; } = "";
    public decimal NoCu { get; set; }
    public decimal TraNoCu { get; set; }
    public decimal TongNoMoi { get; set; }
    public decimal TongDaTra { get; set; }
    public decimal ConNo => NoCu - TraNoCu + TongNoMoi - TongDaTra;

    /// <summary>
    /// Nợ/Trả theo từng ngày: key = Date, value = (Nợ, Trả)
    /// </summary>
    public Dictionary<DateTime, (decimal No, decimal Tra)> ChiTietTheoNgay { get; set; } = new();
}
