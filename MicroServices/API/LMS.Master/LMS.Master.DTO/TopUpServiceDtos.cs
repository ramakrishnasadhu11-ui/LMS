using System;
using System.Collections.Generic;

namespace LMS.Master.DTO
{
    public class TopUpServiceApplicabilityDto
    {
        public int Id { get; set; }

        public int TopUpServiceMasterId { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string ServiceType { get; set; }

        public string Category { get; set; }

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

        public string TopUpName { get; set; }

        /// <summary>
        /// PerGarment or PerOrder.
        /// </summary>
        public string ChargeType { get; set; }

        public decimal UnitPrice { get; set; }

        public string Description { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsArchived { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }

        public List<TopUpServiceApplicabilityDto> Applicability { get; set; } = new List<TopUpServiceApplicabilityDto>();
    }

    public class LaundryOrderTopUpDto
    {
        public int Id { get; set; }

        public int LaundryOrderId { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string OrderNo { get; set; }

        public int LineNo { get; set; }

        public string TopUpName { get; set; }

        /// <summary>
        /// PerGarment or PerOrder snapshot value.
        /// </summary>
        public string ChargeType { get; set; }

        public string ServiceType { get; set; }

        public string Category { get; set; }

        public string ItemName { get; set; }

        public int PieceNo { get; set; }

        public decimal AppliedQuantity { get; set; }

        public decimal UnitPriceSnapshot { get; set; }

        public decimal LineAmountSnapshot { get; set; }

        public DateTime CreatedDate { get; set; }
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

    public class GetTopUpServiceMastersRequestDto
    {
        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public bool IncludeInactive { get; set; }
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
}
