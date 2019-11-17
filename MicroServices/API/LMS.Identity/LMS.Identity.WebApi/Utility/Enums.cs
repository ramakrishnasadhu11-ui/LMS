using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Imagine.Traffic.Api.Utility
{
    public enum SystemSettingTypes
    {
        SpotPlacementExceptions = 2,
        InvoiceHandlingType = 3
    }
    public enum ContactTypes
    {
        Primary = 1,
        Billing = 2,
        Other = 3
    }
    public enum CustomerTypes
    {
        Advertiser = 1,
        Agency = 2,
        RepFirm = 3
    }
    public enum CodeType
    {
        BillingPeriod = 1,
        InvoiceTerms = 2,
        BillingCategory = 3,
        InvoiceHandlingType = 4,
        RevenueType = 5,
        Vendor = 6
    }
    public enum SettingCategory
    {
        SpotPlacementException = 2,
        InvoiceHandlingType = 3
    }

    public enum DayOfWeek
    {
        Monday = 0,
        Tuesday = 1,
        Wednesday = 2,
        Thursday = 3,
        Friday = 4,
        Saturday = 5,
        Sunday = 6
    }
}


