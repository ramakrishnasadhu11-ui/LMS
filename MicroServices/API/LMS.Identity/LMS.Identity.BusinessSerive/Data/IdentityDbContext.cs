using LMS.Identity.DTO.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LMS.Identity.BusinessSerive.Data
{
    public class IdentityDbContext : DbContext
    {
        public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
            : base(options)
        {
        }

        public DbSet<TenantEntity> Tenants => Set<TenantEntity>();

        public DbSet<TenantStoreInfoEntity> TenantStoreInfos => Set<TenantStoreInfoEntity>();

        public DbSet<StoreUserEntity> StoreUsers => Set<StoreUserEntity>();

        public DbSet<StoreConfigurationEntity> StoreConfigurations => Set<StoreConfigurationEntity>();

        public DbSet<TenantStoreStatusEntity> TenantStoreStatuses => Set<TenantStoreStatusEntity>();

        public DbSet<SuperAdminEntity> SuperAdmins => Set<SuperAdminEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TenantEntity>(entity =>
            {
                entity.ToTable("Tenants");
                entity.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
                entity.Property(x => x.TenantName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
                entity.Property(x => x.Address).HasMaxLength(512);
                entity.Property(x => x.City).HasMaxLength(128);
                entity.Property(x => x.Region).HasMaxLength(128);
                entity.Property(x => x.Zip).HasMaxLength(32);
                entity.Property(x => x.Country).HasMaxLength(128);
                entity.Property(x => x.ApprovalStatus).HasMaxLength(32);
                entity.Property(x => x.ApprovalReason).HasMaxLength(512);
                entity.Property(x => x.ApprovedBy).HasMaxLength(256);

                entity.HasIndex(x => x.Email).IsUnique();
                entity.HasIndex(x => x.TenantId).IsUnique();
                entity.HasIndex(x => x.ApprovalStatus);
            });

            modelBuilder.Entity<TenantStoreInfoEntity>(entity =>
            {
                entity.ToTable("TenantStoreInfos");
                entity.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(256).IsRequired();
                entity.Property(x => x.Password).HasMaxLength(256).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(64).IsRequired();
                entity.Property(x => x.Storecodes)
                    .HasConversion(
                        value => string.Join(',', value ?? new List<string>()),
                        value => string.IsNullOrWhiteSpace(value)
                            ? new List<string>()
                            : value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
                    .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                        (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
                        value => value == null ? 0 : value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                        value => value == null ? new List<string>() : value.ToList()));

                entity.HasIndex(x => x.TenantId).IsUnique();
            });

            modelBuilder.Entity<StoreUserEntity>(entity =>
            {
                entity.ToTable("StoreUsers");
                entity.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(32).IsRequired();
                entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
                entity.Property(x => x.Password).HasMaxLength(256).IsRequired();
                entity.Property(x => x.Role).HasMaxLength(32).IsRequired();
                entity.Property(x => x.CreatedBy).HasMaxLength(256);
                entity.Property(x => x.CreatedDate).HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(x => new { x.TenantId, x.StoreCode, x.Email }).IsUnique();
            });

            modelBuilder.Entity<StoreConfigurationEntity>(entity =>
            {
                entity.ToTable("StoreConfigurations");
                entity.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(32).IsRequired();
                entity.Property(x => x.ConfigurationJson).HasColumnType("nvarchar(max)").IsRequired();
                entity.Property(x => x.CreatedBy).HasMaxLength(256);
                entity.Property(x => x.ModifiedBy).HasMaxLength(256);
                entity.Property(x => x.CreatedDate).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(x => x.ModifiedDate).HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(x => new { x.TenantId, x.StoreCode }).IsUnique();
            });

            modelBuilder.Entity<TenantStoreStatusEntity>(entity =>
            {
                entity.ToTable("TenantStoreStatuses");
                entity.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(32).IsRequired();
                // Always send the CLR value on insert. Some existing databases do not have
                // the default constraint, so relying on the store-generated default can insert NULL.
                entity.Property(x => x.IsActive).HasDefaultValue(false).ValueGeneratedNever();
                entity.Property(x => x.ActivatedBy).HasMaxLength(256);
                entity.Property(x => x.CreatedDate).HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(x => new { x.TenantId, x.StoreCode }).IsUnique();
            });

            modelBuilder.Entity<SuperAdminEntity>(entity =>
            {
                entity.ToTable("SuperAdmins");
                entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
                entity.Property(x => x.Password).HasMaxLength(512).IsRequired();
                entity.Property(x => x.DisplayName).HasMaxLength(128);
                entity.Property(x => x.IsActive).HasDefaultValue(true);
                entity.Property(x => x.IsPasswordChanged).HasDefaultValue(false);
                entity.Property(x => x.CreatedDate).HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(x => x.Email).IsUnique();
            });
        }
    }
}
