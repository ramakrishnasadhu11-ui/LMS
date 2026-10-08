using LMSClientFactory.Helper;
using Microsoft.Extensions.Options;

namespace LMSWebUI.Services
{
    public class CustomerApiClient : HttpClientApi, ICustomerApiClient
    {
        public CustomerApiClient(IOptions<ServiceEndpointsOptions> options)
            : base(options.Value.Master.BaseUrl.TrimEnd('/') + "/Customer")
        {
        }
    }
}
