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

        public DbSet<SubscriptionPlanEntity> SubscriptionPlans => Set<SubscriptionPlanEntity>();

        public DbSet<TenantSubscriptionPaymentOrderEntity> TenantSubscriptionPaymentOrders => Set<TenantSubscriptionPaymentOrderEntity>();

        public DbSet<TenantSubscriptionPaymentTransactionEntity> TenantSubscriptionPaymentTransactions => Set<TenantSubscriptionPaymentTransactionEntity>();

        public DbSet<TenantSubscriptionWebhookEventEntity> TenantSubscriptionWebhookEvents => Set<TenantSubscriptionWebhookEventEntity>();

        public DbSet<TenantSubscriptionStateHistoryEntity> TenantSubscriptionStateHistories => Set<TenantSubscriptionStateHistoryEntity>();

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
                entity.Property(x => x.PlanCode).HasMaxLength(32);
                entity.Property(x => x.SubscriptionStatus).HasMaxLength(32);
                entity.Property(x => x.SubscriptionStartUtc).HasColumnType("datetime2");
                entity.Property(x => x.SubscriptionEndUtc).HasColumnType("datetime2");
                entity.Property(x => x.GraceEndUtc).HasColumnType("datetime2");

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
                entity.Property(x => x.CurrencyCode).HasMaxLength(16).HasColumnType("nvarchar(16)");
                entity.Property(x => x.CurrencySymbol).HasMaxLength(16).HasColumnType("nvarchar(16)");
                entity.Property(x => x.CurrencyLocale).HasMaxLength(32).HasColumnType("nvarchar(32)");
                entity.Property(x => x.CurrencyDecimalDigits).HasColumnType("int");
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
                entity.Property(x => x.CurrencyCode).HasMaxLength(16).HasColumnType("nvarchar(16)");
                entity.Property(x => x.CurrencySymbol).HasMaxLength(16).HasColumnType("nvarchar(16)");
                entity.Property(x => x.CurrencyLocale).HasMaxLength(32).HasColumnType("nvarchar(32)");
                entity.Property(x => x.CurrencyDecimalDigits).HasColumnType("int");
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

            modelBuilder.Entity<SubscriptionPlanEntity>(entity =>
            {
                entity.ToTable("SubscriptionPlans");
                entity.Property(x => x.PlanCode).HasMaxLength(32).HasColumnType("nvarchar(32)").IsRequired();
                entity.Property(x => x.BillingCycle).HasMaxLength(16).HasColumnType("nvarchar(16)").IsRequired();
                entity.Property(x => x.Amount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.Currency).HasMaxLength(8).HasColumnType("nvarchar(8)").HasDefaultValue("INR").IsRequired();
                entity.Property(x => x.IsActive).HasColumnType("bit");
                entity.Property(x => x.CreatedDateUtc).HasColumnType("datetime2");
                entity.Property(x => x.ModifiedDateUtc).HasColumnType("datetime2");
                entity.Property(x => x.UpdatedBy).HasMaxLength(256).HasColumnType("nvarchar(256)");

                entity.HasIndex(x => new { x.PlanCode, x.BillingCycle }).IsUnique();
            });

            modelBuilder.Entity<TenantSubscriptionPaymentOrderEntity>(entity =>
            {
                entity.ToTable("TenantSubscriptionPaymentOrders", table =>
                {
                    table.HasCheckConstraint("CK_TenantSubscriptionPaymentOrders_Amount", "[Amount] > 0");
                    table.HasCheckConstraint("CK_TenantSubscriptionPaymentOrders_Expiry", "[OrderExpiresUtc] IS NULL OR [OrderExpiresUtc] >= [CreatedDateUtc]");
                });
                entity.Property(x => x.TenantId).HasMaxLength(64).HasColumnType("nvarchar(64)").IsRequired();
                entity.Property(x => x.TenantEntityId).HasColumnType("int").IsRequired();
                entity.Property(x => x.PlanCode).HasMaxLength(32).HasColumnType("nvarchar(32)").IsRequired();
                entity.Property(x => x.BillingCycle).HasMaxLength(16).HasColumnType("nvarchar(16)").IsRequired();
                entity.Property(x => x.Amount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.Currency).HasMaxLength(8).HasColumnType("nvarchar(8)").HasDefaultValue("INR").IsRequired();
                entity.Property(x => x.OrderStatus).HasMaxLength(32).HasColumnType("nvarchar(32)").IsRequired();
                entity.Property(x => x.GatewayOrderId).HasMaxLength(128).HasColumnType("nvarchar(128)");
                entity.Property(x => x.ReceiptId).HasMaxLength(128).HasColumnType("nvarchar(128)");
                entity.Property(x => x.CreatedDateUtc).HasColumnType("datetime2");
                entity.Property(x => x.ModifiedDateUtc).HasColumnType("datetime2");
                entity.Property(x => x.OrderExpiresUtc).HasColumnType("datetime2");
                entity.Property(x => x.PendingVerificationStatus).HasMaxLength(32).HasColumnType("nvarchar(32)");
                entity.Property(x => x.PendingVerificationUntilUtc).HasColumnType("datetime2");
                entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

                entity.HasIndex(x => new { x.TenantId, x.CreatedDateUtc });
                entity.HasIndex(x => x.GatewayOrderId).IsUnique().HasFilter("[GatewayOrderId] IS NOT NULL");
                entity.HasIndex(x => x.ReceiptId).IsUnique().HasFilter("[ReceiptId] IS NOT NULL");
                entity.HasIndex(x => x.TenantEntityId);
                entity.HasIndex(x => x.TenantId)
                    .IsUnique()
                    .HasFilter("[OrderStatus] IN ('Pending','Created','PendingVerification')")
                    .HasDatabaseName("UX_TenantSubscriptionPaymentOrders_TenantId_Open");

                entity.HasOne<TenantEntity>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantEntityId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<TenantSubscriptionPaymentTransactionEntity>(entity =>
            {
                entity.ToTable("TenantSubscriptionPaymentTransactions", table =>
                {
                    table.HasCheckConstraint("CK_TenantSubscriptionPaymentTransactions_Amount", "[Amount] > 0");
                });
                entity.Property(x => x.TenantId).HasMaxLength(64).HasColumnType("nvarchar(64)").IsRequired();
                entity.Property(x => x.TenantEntityId).HasColumnType("int").IsRequired();
                entity.Property(x => x.GatewayPaymentId).HasMaxLength(128).HasColumnType("nvarchar(128)");
                entity.Property(x => x.GatewayOrderId).HasMaxLength(128).HasColumnType("nvarchar(128)");
                entity.Property(x => x.PaymentProvider).HasMaxLength(32).HasColumnType("nvarchar(32)").IsRequired();
                entity.Property(x => x.IsLiveMode).HasColumnType("bit");
                entity.Property(x => x.TransactionStatus).HasMaxLength(32).HasColumnType("nvarchar(32)").IsRequired();
                entity.Property(x => x.Amount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.Currency).HasMaxLength(8).HasColumnType("nvarchar(8)").HasDefaultValue("INR").IsRequired();
                entity.Property(x => x.CreatedDateUtc).HasColumnType("datetime2");
                entity.Property(x => x.PaidAtUtc).HasColumnType("datetime2");
                entity.Property(x => x.VerifiedAtUtc).HasColumnType("datetime2");
                entity.Property(x => x.IsSignatureVerified).HasColumnType("bit");
                entity.Property(x => x.FailureCode).HasMaxLength(64).HasColumnType("nvarchar(64)");
                entity.Property(x => x.FailureReason).HasMaxLength(512).HasColumnType("nvarchar(512)");
                entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

                entity.HasIndex(x => x.TenantSubscriptionPaymentOrderId);
                entity.HasIndex(x => x.TenantId);
                entity.HasIndex(x => x.TenantEntityId);
                entity.HasIndex(x => new { x.PaymentProvider, x.IsLiveMode, x.GatewayPaymentId })
                    .IsUnique()
                    .HasFilter("[GatewayPaymentId] IS NOT NULL")
                    .HasDatabaseName("UX_TenantSubscriptionPaymentTransactions_Provider_Mode_PaymentId");
                entity.HasOne<TenantSubscriptionPaymentOrderEntity>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantSubscriptionPaymentOrderId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<TenantEntity>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantEntityId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<TenantSubscriptionWebhookEventEntity>(entity =>
            {
                entity.ToTable("TenantSubscriptionWebhookEvents");
                entity.Property(x => x.TenantId).HasMaxLength(64).HasColumnType("nvarchar(64)").IsRequired();
                entity.Property(x => x.TenantEntityId).HasColumnType("int").IsRequired();
                entity.Property(x => x.EventId).HasMaxLength(128).HasColumnType("nvarchar(128)");
                entity.Property(x => x.PaymentProvider).HasMaxLength(32).HasColumnType("nvarchar(32)").IsRequired();
                entity.Property(x => x.IsLiveMode).HasColumnType("bit");
                entity.Property(x => x.EventType).HasMaxLength(128).HasColumnType("nvarchar(128)").IsRequired();
                entity.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
                entity.Property(x => x.ProcessingStatus).HasMaxLength(32).HasColumnType("nvarchar(32)").IsRequired();
                entity.Property(x => x.ProcessingError).HasMaxLength(1024).HasColumnType("nvarchar(1024)");
                entity.Property(x => x.ReceivedAtUtc).HasColumnType("datetime2");
                entity.Property(x => x.ProcessedAtUtc).HasColumnType("datetime2");
                entity.Property(x => x.IsSignatureVerified).HasColumnType("bit");
                entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

                entity.HasIndex(x => new { x.PaymentProvider, x.IsLiveMode, x.EventId })
                    .IsUnique()
                    .HasFilter("[EventId] IS NOT NULL")
                    .HasDatabaseName("UX_TenantSubscriptionWebhookEvents_Provider_Mode_EventId");
                entity.HasIndex(x => x.TenantId);
                entity.HasIndex(x => x.TenantEntityId);
                entity.HasIndex(x => x.TenantSubscriptionPaymentOrderId);

                entity.HasOne<TenantSubscriptionPaymentOrderEntity>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantSubscriptionPaymentOrderId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<TenantEntity>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantEntityId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<TenantSubscriptionStateHistoryEntity>(entity =>
            {
                entity.ToTable("TenantSubscriptionStateHistories");
                entity.Property(x => x.TenantId).HasMaxLength(64).HasColumnType("nvarchar(64)").IsRequired();
                entity.Property(x => x.TenantEntityId).HasColumnType("int").IsRequired();
                entity.Property(x => x.PreviousSubscriptionStatus).HasMaxLength(32).HasColumnType("nvarchar(32)");
                entity.Property(x => x.NewSubscriptionStatus).HasMaxLength(32).HasColumnType("nvarchar(32)").IsRequired();
                entity.Property(x => x.PreviousPlanCode).HasMaxLength(32).HasColumnType("nvarchar(32)");
                entity.Property(x => x.NewPlanCode).HasMaxLength(32).HasColumnType("nvarchar(32)");
                entity.Property(x => x.ChangedBy).HasMaxLength(256).HasColumnType("nvarchar(256)");
                entity.Property(x => x.ChangeReason).HasMaxLength(256).HasColumnType("nvarchar(256)");
                entity.Property(x => x.ActivationIdempotencyKey).HasMaxLength(200).HasColumnType("nvarchar(200)");
                entity.Property(x => x.ChangedAtUtc).HasColumnType("datetime2");
                entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

                entity.HasIndex(x => new { x.TenantId, x.ChangedAtUtc });
                entity.HasIndex(x => x.TenantEntityId);
                entity.HasIndex(x => x.TenantSubscriptionPaymentOrderId);
                entity.HasIndex(x => x.ActivationIdempotencyKey)
                    .IsUnique()
                    .HasFilter("[ActivationIdempotencyKey] IS NOT NULL")
                    .HasDatabaseName("UX_TenantSubscriptionStateHistories_ActivationKey");

                entity.HasOne<TenantSubscriptionPaymentOrderEntity>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantSubscriptionPaymentOrderId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<TenantEntity>()
                    .WithMany()
                    .HasForeignKey(x => x.TenantEntityId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
