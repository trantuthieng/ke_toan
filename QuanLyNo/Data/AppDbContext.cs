using Microsoft.EntityFrameworkCore;
using QuanLyNo.Models;

namespace QuanLyNo.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<GiaoDich> GiaoDichs => Set<GiaoDich>();
    public DbSet<TraNo> TraNos => Set<TraNo>();
    public DbSet<KhachHang> KhachHangs => Set<KhachHang>();
    public DbSet<ImageImportBatch> ImageImportBatches => Set<ImageImportBatch>();
    public DbSet<ImageImportRow> ImageImportRows => Set<ImageImportRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GiaoDich>(entity =>
        {
            entity.HasIndex(e => e.Ngay);
            entity.HasIndex(e => e.TenKhach);
            entity.HasIndex(e => e.TenLai);
            entity.HasIndex(e => e.NguonBanHang);
            entity.HasIndex(e => e.ImageImportRowId);
        });

        modelBuilder.Entity<TraNo>(entity =>
        {
            entity.HasIndex(e => e.NgayTra);
            entity.HasIndex(e => e.TenKhach);
            entity.HasIndex(e => e.TenLai);
            entity.HasIndex(e => e.NguonBanHang);
            entity.HasIndex(e => e.ImageImportRowId);
        });

        modelBuilder.Entity<KhachHang>(entity =>
        {
            entity.HasIndex(e => e.TenKhach).IsUnique();
        });

        modelBuilder.Entity<ImageImportBatch>(entity =>
        {
            entity.HasIndex(e => e.Ngay);
            entity.HasIndex(e => e.LoaiImport);
            entity.HasIndex(e => e.NguonBanHang);
            entity.HasMany(e => e.Rows)
                .WithOne(e => e.Batch)
                .HasForeignKey(e => e.ImageImportBatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ImageImportRow>(entity =>
        {
            entity.HasIndex(e => e.ImageImportBatchId);
            entity.HasIndex(e => e.ImageOrder);
            entity.HasIndex(e => e.ReviewStatus);
            entity.HasOne(e => e.MatchedGiaoDich)
                .WithMany()
                .HasForeignKey(e => e.MatchedGiaoDichId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.MatchedTraNo)
                .WithMany()
                .HasForeignKey(e => e.MatchedTraNoId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
