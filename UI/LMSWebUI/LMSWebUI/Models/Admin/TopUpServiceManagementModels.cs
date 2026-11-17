using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace LMSWebUI.Models.Admin
{
    public class TopUpServiceApplicabilityDto
    {
        public int Id { get; set; }

        public int TopUpServiceMasterId { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        [StringLength(128)]
        public string ServiceType { get; set; }

        [StringLength(128)]
        public string Category { get; set; }

        [StringLength(128)]
        public string ItemName { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }
    }

    public class TopUpServiceMasterDto
    {
        public int Id { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        [Required]
        [StringLength(128)]
        public string TopUpName { get; set; }

        [Required]
        [StringLength(32)]
        public string ChargeType { get; set; }

        [Range(0, 1000000)]
        public decimal UnitPrice { get; set; }

        [StringLength(512)]
        public string Description { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsArchived { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }

        public List<TopUpServiceApplicabilityDto> Applicability { get; set; } = new List<TopUpServiceApplicabilityDto>();
    }

    public class SaveTopUpServiceMasterRequestDto
    {
        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public TopUpServiceMasterDto TopUpService { get; set; } = new TopUpServiceMasterDto();
    }

    public class SaveTopUpServiceMasterResponseDto
    {
        public string StatusCode { get; set; }

        public string Message { get; set; }

        public TopUpServiceMasterDto TopUpService { get; set; }
    }

    public class GetTopUpServiceMastersResponseDto
    {
        public string StatusCode { get; set; }

        public List<TopUpServiceMasterDto> TopUpServices { get; set; } = new List<TopUpServiceMasterDto>();
    }

    public class SetTopUpServiceStatusRequestDto
    {
        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string TopUpName { get; set; }

        public bool IsActive { get; set; }

        public bool IsArchived { get; set; }
    }

    public class SetTopUpServiceStatusResponseDto
    {
        public string StatusCode { get; set; }

        public string Message { get; set; }

        public TopUpServiceMasterDto TopUpService { get; set; }
    }

    public class GetApplicableTopUpsRequestDto
    {
        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string ServiceType { get; set; }

        public string Category { get; set; }

        public string ItemName { get; set; }

        public string OrderMode { get; set; }
    }

    public class GetApplicableTopUpsResponseDto
    {
        public string StatusCode { get; set; }

        public List<TopUpServiceMasterDto> TopUpServices { get; set; } = new List<TopUpServiceMasterDto>();
    }

    public class TopUpServicesManagementViewModel
    {
        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public bool IncludeInactive { get; set; }

        public List<TopUpServiceMasterDto> TopUpServices { get; set; } = new List<TopUpServiceMasterDto>();
    }
}
