using Microsoft.EntityFrameworkCore;
using QuanLyNo.Models;

namespace QuanLyNo.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<GiaoDich> GiaoDichs => Set<GiaoDich>();
    public DbSet<TraNo> TraNos => Set<TraNo>();
    public DbSet<KhachHang> KhachHangs => Set<KhachHang>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GiaoDich>(entity =>
        {
            entity.HasIndex(e => e.Ngay);
            entity.HasIndex(e => e.TenKhach);
            entity.HasIndex(e => e.TenLai);
        });

        modelBuilder.Entity<TraNo>(entity =>
        {
            entity.HasIndex(e => e.NgayTra);
            entity.HasIndex(e => e.TenKhach);
        });

        modelBuilder.Entity<KhachHang>(entity =>
        {
            entity.HasIndex(e => e.TenKhach).IsUnique();
        });
    }
}
