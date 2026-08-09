using Microsoft.EntityFrameworkCore;
using ShipStation.Integration.Persistence.Entities;

namespace ShipStation.Integration.Persistence;

public sealed class ShipStationDbContext : DbContext
{
    public ShipStationDbContext(DbContextOptions<ShipStationDbContext> options)
        : base(options)
    {
    }

    public DbSet<OrderRecord> Orders => Set<OrderRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var order = modelBuilder.Entity<OrderRecord>();

        order.ToTable("shipstation_orders");
        order.HasKey(record => record.OrderId);

        order.Property(record => record.OrderId).HasColumnName("order_id").ValueGeneratedNever();
        order.Property(record => record.OrderNumber).HasColumnName("order_number").IsRequired();
        order.Property(record => record.OrderKey).HasColumnName("order_key");
        order.Property(record => record.OrderStatus).HasColumnName("order_status").IsRequired();
        order.Property(record => record.CustomerEmail).HasColumnName("customer_email");
        order.Property(record => record.OrderTotal).HasColumnName("order_total").HasColumnType("numeric(18,2)");
        order.Property(record => record.OrderDate).HasColumnName("order_date");
        order.Property(record => record.ModifyDate).HasColumnName("modify_date");
        order.Property(record => record.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        order.Property(record => record.SyncedAt).HasColumnName("synced_at");

        // order_key is what upstream systems join on, and ShipStation treats it as
        // unique, but it is nullable — so the index has to be partial or every row
        // without a key collides with every other.
        order.HasIndex(record => record.OrderKey)
            .HasDatabaseName("ix_shipstation_orders_order_key")
            .IsUnique()
            .HasFilter("order_key IS NOT NULL");

        // Incremental syncs read by watermark; without this they seq-scan.
        order.HasIndex(record => record.ModifyDate)
            .HasDatabaseName("ix_shipstation_orders_modify_date");

        order.HasIndex(record => record.OrderStatus)
            .HasDatabaseName("ix_shipstation_orders_order_status");
    }
}
