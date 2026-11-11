using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bimbel.Data;

/// <summary>
/// Hanya dipakai oleh perintah `dotnet ef` (design-time),
/// misalnya untuk membuat migrasi. Tidak dipakai saat test berjalan.
/// </summary>
public class BimbelDbContextFactory : IDesignTimeDbContextFactory<BimbelDbContext>
{
    public BimbelDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("BIMBEL_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=bimbel;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<BimbelDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BimbelDbContext(options);
    }
}
