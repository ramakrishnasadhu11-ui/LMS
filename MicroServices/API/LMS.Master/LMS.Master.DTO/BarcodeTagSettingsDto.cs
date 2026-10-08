namespace LMS.Master.DTO
{
    public class BarcodeTagSettingsDto
    {
        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public bool EnableTagging { get; set; }

        public string TagPrefix { get; set; }

        public int NextTagNumber { get; set; }

        public int TagNumberPadding { get; set; }

        public bool ResetTagNumberYearly { get; set; }

        public string Notes { get; set; }
    }
}
