using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace LMS.Identity.DTO
{
    /// <summary>
    /// IOResponse Model
    /// </summary>
  
    public class IOResponse
    {
         /// <summary>
        ///  Message
        /// </summary>
        [StringLength(maximumLength: int.MaxValue)]
        public string Message { get; set; }

        /// <summary>
        ///  TenantId
        /// </summary>
        [StringLength(maximumLength: int.MaxValue)]
        public string TenantId { get; set; }
       
        /// <summary>
        ///  TenantName
        /// </summary>
       
        [StringLength(maximumLength: int.MaxValue)]
        public string TenantName { get; set; }
       
         /// <summary>
        ///  Email
        /// </summary>
       
        [StringLength(maximumLength: int.MaxValue)]
        public string Email { get; set; }

        /// <summary>
        ///  StatusCode
        /// </summary>
        [StringLength(maximumLength: int.MaxValue)]
        public string StatusCode { get; set; }

        /// <summary>
        ///  StoreCodea
        /// </summary>
        public List<string> Storecodes { get; set; }

        /// <summary>
        ///  PhoneNumber
        /// </summary>
        public string PhoneNumber {get;set;}

        /// <summary>
        ///  Address
        /// </summary>
         public string Address {get;set;}

        /// <summary>
        ///  Country
        /// </summary>
         public string Country {get;set;}

        /// <summary>
        /// Tenant membership start date
        /// </summary>
        public DateTime? CreatedDate { get; set; }

        /// <summary>
        /// UserRole
        /// </summary>
        public string UserRole { get; set; }

        /// <summary>
        /// Indicates that the caller must force the user to change their password (first-login)
        /// </summary>
        public bool? MustChangePassword { get; set; }

        /// <summary>
        /// Store configuration payload
        /// </summary>
        public string ConfigurationJson { get; set; }

        /// <summary>
        /// Activation state for each tenant store
        /// </summary>
        public List<TenantStoreStatusDto> StoreStatuses { get; set; }

        /// <summary>
        /// Approval state for each tenant, used by the super admin approval screen
        /// </summary>
        public List<TenantApprovalDto> TenantApprovals { get; set; }

    }

    /// <summary>
    /// Approval state of a single tenant
    /// </summary>
    public class TenantApprovalDto
    {
        public string TenantId { get; set; }

        public string TenantName { get; set; }

        public string Email { get; set; }

        public string PhoneNumber { get; set; }

        public string City { get; set; }

        public string Country { get; set; }

        /// <summary>Pending, Approved or Rejected.</summary>
        public string ApprovalStatus { get; set; }

        public string ApprovalReason { get; set; }

        public string ApprovedBy { get; set; }

        public DateTime? ApprovalDate { get; set; }

        public DateTime CreatedDate { get; set; }

        /// <summary>Number of stores mapped to the tenant, shown for context.</summary>
        public int StoreCount { get; set; }
    }

    /// <summary>
    /// Activation state of a single tenant store
    /// </summary>
    public class TenantStoreStatusDto
    {
        public string StoreCode { get; set; }

        public bool IsActive { get; set; }

        public string ActivatedBy { get; set; }

        public DateTime? ActivatedDate { get; set; }

        /// <summary>Owning tenant, populated for cross-tenant super admin views.</summary>
        public string TenantId { get; set; }

        public string TenantName { get; set; }

        public string TenantEmail { get; set; }
    }
}
