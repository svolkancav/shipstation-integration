using Microsoft.EntityFrameworkCore;
using ShipStation.Core.Entities;

namespace ShipStation.DataAccess;

public class AppDatabaseContext(DbContextOptions<AppDatabaseContext> options) : DbContext(options)
{
    public DbSet<ShipStationOrder> ShipStationOrders { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var order = modelBuilder.Entity<ShipStationOrder>();

        order.Property(entity => entity.OrderId).ValueGeneratedNever();
        order.Property(entity => entity.OrderTotal).HasPrecision(18, 2);

        // Persist the enum by name. Storing it as an int couples the column to the
        // declaration order, so reordering the enum would silently reinterpret every
        // stored row.
        order.Property(entity => entity.OrderStatus).HasConversion<string>().HasMaxLength(32);

        // jsonb is worth the column type on PostgreSQL, but the tests run on SQLite
        // where it does not exist — so it is applied per provider rather than
        // unconditionally.
        if (Database.IsNpgsql())
        {
            order.Property(entity => entity.Payload).HasColumnType("jsonb");
        }

        // OrderKey is unique where it is set, but it is nullable: an unfiltered
        // unique index would make every keyless order collide with every other.
        order.HasIndex(entity => entity.OrderKey)
            .IsUnique()
            .HasFilter(Database.IsNpgsql() ? "\"OrderKey\" IS NOT NULL" : null);

        // Incremental syncs read by watermark; without this they seq-scan.
        order.HasIndex(entity => entity.ModifyDate);
        order.HasIndex(entity => entity.OrderStatus);
    }
}
