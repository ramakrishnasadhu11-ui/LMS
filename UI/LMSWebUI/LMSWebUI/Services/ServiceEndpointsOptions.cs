namespace LMSWebUI.Services
{
    public class ServiceEndpointsOptions
    {
        public ServiceEndpointOptions Identity { get; set; } = new ServiceEndpointOptions();

        public ServiceEndpointOptions Master { get; set; } = new ServiceEndpointOptions();
    }

    public class ServiceEndpointOptions
    {
        public string BaseUrl { get; set; } = string.Empty;
    }
}
