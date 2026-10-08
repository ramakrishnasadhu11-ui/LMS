using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO.Entities
{
    public class TenantEntity
    {
        [Key]
        public int Id { get; set; }

        public int NoOfStores { get; set; }
       
        public string TenantId { get; set; }
        
        public string TenantName { get; set; }

        public string Email {get;set;}

       public string PhoneNumber { get; set; }
      
       public string MiddleName { get; set; }

       public string FamilyName { get; set; }

       public string Address { get; set; }
        
       public string City { get; set; }

       public string Region { get; set; }

       public string Zip { get; set; }

       public string Country { get; set; }

       public DateTime CreatedDate { get; set; }

       public DateTime ModifiedDate { get; set; }

       /// <summary>
       /// Approval state of the tenant: Pending, Approved or Rejected.
       /// New tenants start as Pending and cannot log in until a super admin approves them.
       /// </summary>
       [MaxLength(32)]
       public string ApprovalStatus { get; set; }

       /// <summary>
       /// Reason captured when a super admin rejects the tenant.
       /// </summary>
       [MaxLength(512)]
       public string ApprovalReason { get; set; }

       [MaxLength(256)]
       public string ApprovedBy { get; set; }

       public DateTime? ApprovalDate { get; set; }

    }
}
