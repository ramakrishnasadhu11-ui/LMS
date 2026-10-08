using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Master.DTO.Entity
{
    public class BarcodeTagSettingsEntity
    {
        [Key]
        public int Id { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public bool EnableTagging { get; set; }

        public string TagPrefix { get; set; }

        public int NextTagNumber { get; set; }

        public int TagNumberPadding { get; set; }

        public bool ResetTagNumberYearly { get; set; }

        public string Notes { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }
    }
}
