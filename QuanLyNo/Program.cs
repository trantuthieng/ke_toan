using Microsoft.EntityFrameworkCore;
using QuanLyNo.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    EnsureColumn(db, "KhachHangs", "TraNoCu", "TEXT NOT NULL DEFAULT '0'");
    EnsureColumn(db, "GiaoDichs", "SoLuongAnh", "TEXT NULL");
    EnsureColumn(db, "GiaoDichs", "NguonBanHang", "TEXT NULL");
    EnsureColumn(db, "GiaoDichs", "ImageOrder", "INTEGER NULL");
    EnsureColumn(db, "GiaoDichs", "ImageImportRowId", "INTEGER NULL");
    EnsureColumn(db, "GiaoDichs", "ReviewStatus", "TEXT NULL");
    EnsureColumn(db, "TraNos", "TenLai", "TEXT NULL");
    EnsureColumn(db, "TraNos", "NguonBanHang", "TEXT NULL");
    EnsureColumn(db, "TraNos", "ImageOrder", "INTEGER NULL");
    EnsureColumn(db, "TraNos", "ImageImportRowId", "INTEGER NULL");
    EnsureColumn(db, "TraNos", "ReviewStatus", "TEXT NULL");
    EnsureImageImportSchema(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static void EnsureColumn(AppDbContext db, string tableName, string columnName, string columnDefinition)
{
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State == System.Data.ConnectionState.Closed;
    if (shouldClose) connection.Open();

    try
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }
    finally
    {
        if (shouldClose) connection.Close();
    }
}

static void EnsureImageImportSchema(AppDbContext db)
{
    ExecuteNonQuery(db, """
        CREATE TABLE IF NOT EXISTS ImageImportBatches (
            Id INTEGER NOT NULL CONSTRAINT PK_ImageImportBatches PRIMARY KEY AUTOINCREMENT,
            Ngay TEXT NOT NULL,
            LoaiImport TEXT NOT NULL,
            NguonBanHang TEXT NOT NULL,
            OriginalFileName TEXT NOT NULL,
            StoredFileName TEXT NOT NULL,
            ImagePath TEXT NOT NULL,
            Status TEXT NOT NULL,
            RawText TEXT NULL,
            ParseError TEXT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NULL
        );
        """);

    ExecuteNonQuery(db, """
        CREATE TABLE IF NOT EXISTS ImageImportRows (
            Id INTEGER NOT NULL CONSTRAINT PK_ImageImportRows PRIMARY KEY AUTOINCREMENT,
            ImageImportBatchId INTEGER NOT NULL,
            ImageOrder INTEGER NOT NULL,
            TenLai TEXT NULL,
            TenKhach TEXT NULL,
            SoLuongAnh TEXT NULL,
            SoTienTra TEXT NULL,
            Confidence TEXT NULL,
            RawLine TEXT NULL,
            MatchedGiaoDichId INTEGER NULL,
            MatchedTraNoId INTEGER NULL,
            ReviewStatus TEXT NOT NULL,
            Notes TEXT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NULL,
            CONSTRAINT FK_ImageImportRows_ImageImportBatches_ImageImportBatchId
                FOREIGN KEY (ImageImportBatchId) REFERENCES ImageImportBatches (Id) ON DELETE CASCADE,
            CONSTRAINT FK_ImageImportRows_GiaoDichs_MatchedGiaoDichId
                FOREIGN KEY (MatchedGiaoDichId) REFERENCES GiaoDichs (Id) ON DELETE SET NULL,
            CONSTRAINT FK_ImageImportRows_TraNos_MatchedTraNoId
                FOREIGN KEY (MatchedTraNoId) REFERENCES TraNos (Id) ON DELETE SET NULL
        );
        """);

    EnsureIndex(db, "IX_ImageImportBatches_Ngay", "ImageImportBatches", "Ngay");
    EnsureIndex(db, "IX_ImageImportBatches_LoaiImport", "ImageImportBatches", "LoaiImport");
    EnsureIndex(db, "IX_ImageImportBatches_NguonBanHang", "ImageImportBatches", "NguonBanHang");
    EnsureIndex(db, "IX_ImageImportRows_ImageImportBatchId", "ImageImportRows", "ImageImportBatchId");
    EnsureIndex(db, "IX_ImageImportRows_ImageOrder", "ImageImportRows", "ImageOrder");
    EnsureIndex(db, "IX_ImageImportRows_ReviewStatus", "ImageImportRows", "ReviewStatus");
}

static void EnsureIndex(AppDbContext db, string indexName, string tableName, string columnName)
{
    ExecuteNonQuery(db, $"CREATE INDEX IF NOT EXISTS {indexName} ON {tableName} ({columnName});");
}

static void ExecuteNonQuery(AppDbContext db, string sql)
{
    var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State == System.Data.ConnectionState.Closed;
    if (shouldClose) connection.Open();

    try
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    finally
    {
        if (shouldClose) connection.Close();
    }
}
