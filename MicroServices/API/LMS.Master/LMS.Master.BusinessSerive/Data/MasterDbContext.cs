using LMS.Master.DTO.Entity;
using Microsoft.EntityFrameworkCore;

namespace LMS.Master.BusinessSerive.Data
{
    public class MasterDbContext : DbContext
    {
        public MasterDbContext(DbContextOptions<MasterDbContext> options)
            : base(options)
        {
        }

        public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
        public DbSet<CustomerPreferenceEntity> CustomerPreferences => Set<CustomerPreferenceEntity>();
        public DbSet<PricingRulesEntity> PricingRules => Set<PricingRulesEntity>();
        public DbSet<PaymentSettingsEntity> PaymentSettings => Set<PaymentSettingsEntity>();
        public DbSet<TaxInvoiceSettingsEntity> TaxInvoiceSettings => Set<TaxInvoiceSettingsEntity>();
        public DbSet<BarcodeTagSettingsEntity> BarcodeTagSettings => Set<BarcodeTagSettingsEntity>();
        public DbSet<WorkflowStatusEntity> WorkflowStatuses => Set<WorkflowStatusEntity>();
        public DbSet<CustomerAdvanceEntity> CustomerAdvances => Set<CustomerAdvanceEntity>();
        public DbSet<LaundryOrderEntity> LaundryOrders => Set<LaundryOrderEntity>();
        public DbSet<LaundryOrderItemEntity> LaundryOrderItems => Set<LaundryOrderItemEntity>();
        public DbSet<LaundryItemPriceEntity> LaundryItemPrices => Set<LaundryItemPriceEntity>();
        public DbSet<StoreServiceMasterEntity> StoreServiceMasters => Set<StoreServiceMasterEntity>();
        public DbSet<StoreItemMasterEntity> StoreItemMasters => Set<StoreItemMasterEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CustomerEntity>(entity =>
            {
                entity.ToTable("Customers");
                entity.Property(x => x.CustomerName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.Address).HasMaxLength(512).IsRequired();
                // MembershipId removed system-wide
                entity.Property(x => x.BarCode).HasMaxLength(64);
                entity.Property(x => x.PhoneNumber).HasMaxLength(32);
                entity.Property(x => x.Email).HasMaxLength(256);
                entity.Property(x => x.StoreCode).HasMaxLength(64);
                entity.Property(x => x.TenantName).HasMaxLength(256);
                entity.Property(x => x.CustCode).HasMaxLength(64);

                entity.HasIndex(x => x.CustomerName).IsUnique();
                entity.HasIndex(x => x.CustCode).IsUnique();
            });

            modelBuilder.Entity<CustomerPreferenceEntity>(entity =>
            {
                entity.ToTable("CustomerPreferences");
                entity.Property(x => x.TenantName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.DefaultServiceType).HasMaxLength(64);
                entity.Property(x => x.DefaultPaymentMode).HasMaxLength(64);
                entity.Property(x => x.DefaultStarchLevel).HasMaxLength(32);

                entity.HasIndex(x => new { x.TenantName, x.StoreCode }).IsUnique();
            });

            modelBuilder.Entity<PricingRulesEntity>(entity =>
            {
                entity.ToTable("PricingRules");
                entity.Property(x => x.TenantName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.ExpressSurchargePercent).HasColumnType("decimal(5,2)");
                entity.Property(x => x.MinimumOrderAmount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.Notes).HasMaxLength(300);

                entity.HasIndex(x => new { x.TenantName, x.StoreCode }).IsUnique();
            });

            modelBuilder.Entity<PaymentSettingsEntity>(entity =>
            {
                entity.ToTable("PaymentSettings");
                entity.Property(x => x.TenantName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.DefaultPaymentMode).HasMaxLength(32);
                entity.Property(x => x.CreditLimitAmount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.Notes).HasMaxLength(300);

                entity.HasIndex(x => new { x.TenantName, x.StoreCode }).IsUnique();
            });

            modelBuilder.Entity<TaxInvoiceSettingsEntity>(entity =>
            {
                entity.ToTable("TaxInvoiceSettings");
                entity.Property(x => x.TenantName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.GstPercent).HasColumnType("decimal(5,2)");
                entity.Property(x => x.InvoicePrefix).HasMaxLength(16);
                entity.Property(x => x.Notes).HasMaxLength(300);

                entity.HasIndex(x => new { x.TenantName, x.StoreCode }).IsUnique();
            });

            modelBuilder.Entity<BarcodeTagSettingsEntity>(entity =>
            {
                entity.ToTable("BarcodeTagSettings");
                entity.Property(x => x.TenantName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.TagPrefix).HasMaxLength(16);
                entity.Property(x => x.Notes).HasMaxLength(300);

                entity.HasIndex(x => new { x.TenantName, x.StoreCode }).IsUnique();
            });

            modelBuilder.Entity<WorkflowStatusEntity>(entity =>
            {
                entity.ToTable("WorkflowStatuses");
                entity.Property(x => x.TenantName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.StatusName).HasMaxLength(64).IsRequired();
                entity.Property(x => x.ColorCode).HasMaxLength(16);

                entity.HasIndex(x => new { x.TenantName, x.StoreCode, x.StatusName }).IsUnique();
            });

            modelBuilder.Entity<CustomerAdvanceEntity>(entity =>
            {
                entity.ToTable("CustomerAdvances");
                entity.Property(x => x.TenantName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.CustCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.TransactionType).HasMaxLength(32).IsRequired();
                entity.Property(x => x.Notes).HasMaxLength(500);
                entity.Property(x => x.AdvanceAmount).HasColumnType("decimal(18,2)");

                entity.HasIndex(x => new { x.TenantName, x.StoreCode, x.CustCode });
            });

            modelBuilder.Entity<LaundryOrderEntity>(entity =>
            {
                entity.ToTable("LaundryOrders");
                entity.Property(x => x.TenantName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.CustCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.CustomerName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.OrderNo).HasMaxLength(64).IsRequired();
                entity.Property(x => x.ServiceType).HasMaxLength(64).IsRequired();
                entity.Property(x => x.OrderMode).HasMaxLength(16).IsRequired();
                entity.Property(x => x.PaymentMode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.Notes).HasMaxLength(500);
                entity.Property(x => x.WeightInKg).HasColumnType("decimal(18,3)");
                entity.Property(x => x.RatePerKg).HasColumnType("decimal(18,2)");
                entity.Property(x => x.OrderAmount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.SubTotal).HasColumnType("decimal(18,2)");
                entity.Property(x => x.TaxPercent).HasColumnType("decimal(5,2)");
                entity.Property(x => x.TaxAmount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.CgstAmount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.SgstAmount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.InvoiceNo).HasMaxLength(32);
                entity.Property(x => x.Status).HasMaxLength(64);
                entity.Property(x => x.AdvanceUsed).HasColumnType("decimal(18,2)");
                entity.Property(x => x.NetPayable).HasColumnType("decimal(18,2)");

                entity.HasIndex(x => x.OrderNo).IsUnique();
                entity.HasIndex(x => new { x.TenantName, x.StoreCode, x.CustCode });
            });

            modelBuilder.Entity<LaundryOrderItemEntity>(entity =>
            {
                entity.ToTable("LaundryOrderItems");
                entity.Property(x => x.TenantName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.OrderNo).HasMaxLength(64).IsRequired();
                entity.Property(x => x.ServiceType).HasMaxLength(64);
                entity.Property(x => x.Category).HasMaxLength(64);
                entity.Property(x => x.ItemName).HasMaxLength(128);
                entity.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
                entity.Property(x => x.TagNo).HasMaxLength(32);

                entity.HasIndex(x => x.OrderNo);
                entity.HasIndex(x => new { x.TenantName, x.StoreCode, x.TagNo });
            });

            modelBuilder.Entity<LaundryItemPriceEntity>(entity =>
            {
                entity.ToTable("LaundryItemPrices");
                entity.Property(x => x.TenantName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.ServiceType).HasMaxLength(64).IsRequired();
                entity.Property(x => x.Category).HasMaxLength(64).IsRequired();
                entity.Property(x => x.ItemName).HasMaxLength(128).IsRequired();
                entity.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");

                entity.HasIndex(x => new { x.TenantName, x.StoreCode, x.ServiceType, x.Category, x.ItemName }).IsUnique();
            });

            modelBuilder.Entity<StoreServiceMasterEntity>(entity =>
            {
                entity.ToTable("StoreServiceMaster");
                entity.Property(x => x.TenantName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.MasterType).HasMaxLength(32).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(256);

                entity.HasIndex(x => new { x.TenantName, x.StoreCode, x.MasterType, x.Name }).IsUnique();
            });

            modelBuilder.Entity<StoreItemMasterEntity>(entity =>
            {
                entity.ToTable("StoreItemMaster");
                entity.Property(x => x.TenantName).HasMaxLength(256).IsRequired();
                entity.Property(x => x.StoreCode).HasMaxLength(64).IsRequired();
                entity.Property(x => x.ServiceType).HasMaxLength(64).IsRequired();
                entity.Property(x => x.Category).HasMaxLength(64).IsRequired();
                entity.Property(x => x.ItemName).HasMaxLength(128).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(256);

                entity.HasIndex(x => new { x.TenantName, x.StoreCode, x.ServiceType, x.Category, x.ItemName }).IsUnique();
            });
        }
    }
}
