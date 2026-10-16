using System;
using System.Collections.Generic;

namespace LMS.Identity.DTO
{
    public class StoreConfigurationDto
    {
        public string StoreCode { get; set; }
        public string ConfigurationJson { get; set; }
        public string CurrencyCode { get; set; }
        public string CurrencySymbol { get; set; }
        public string CurrencyLocale { get; set; }
        public int? CurrencyDecimalDigits { get; set; }
    }
}
