using LMSClientFactory.Helper;
using Microsoft.Extensions.Options;

namespace LMSWebUI.Services
{
    public class LoginApiClient : HttpClientApi, ILoginApiClient
    {
        public LoginApiClient(IOptions<ServiceEndpointsOptions> options)
            : base(options.Value.Identity.BaseUrl.TrimEnd('/') + "/Login")
        {
        }
    }
}
