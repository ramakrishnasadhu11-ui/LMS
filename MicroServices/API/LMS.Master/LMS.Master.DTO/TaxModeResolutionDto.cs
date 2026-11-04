namespace LMS.Master.DTO
{
    public class TaxModeResolutionDto
    {
        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string CountryCode { get; set; }

        public string ResolvedTaxMode { get; set; }

        public string ResolutionSource { get; set; }

        public bool LegacyTaxEnabled { get; set; }

        public decimal LegacyGstPercent { get; set; }
    }
}
