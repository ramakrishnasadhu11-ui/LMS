namespace LMS.Master.DTO
{
    public class LaundryDeliveryStatusResponseDto
    {
        public string StatusCode { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public int ReadyForDeliveryCount { get; set; }

        public int PendingDeliveryCount { get; set; }

        public int TotalOrdersConsidered { get; set; }
    }
}
