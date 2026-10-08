using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace LMSWebUI.Models.Customer
{
    [Index(nameof(TenantName), nameof(StoreCode), nameof(OrderNo), nameof(LoggedAtUtc))]
    public class OrderActionAuditLog
    {
        [Key]
        public long Id { get; set; }

        [Required]
        [MaxLength(120)]
        public string TenantName { get; set; }

        [Required]
        [MaxLength(60)]
        public string StoreCode { get; set; }

        [Required]
        [MaxLength(80)]
        public string OrderNo { get; set; }

        [MaxLength(80)]
        public string InvoiceNo { get; set; }

        [Required]
        [MaxLength(80)]
        public string ActionName { get; set; }

        [MaxLength(256)]
        public string ActionMessage { get; set; }

        [MaxLength(120)]
        public string CustomerEmail { get; set; }

        [MaxLength(120)]
        public string UserEmail { get; set; }

        public DateTime LoggedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
