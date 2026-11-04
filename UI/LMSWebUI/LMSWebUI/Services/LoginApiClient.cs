using LMSClientFactory.Helper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LMSWebUI.Services
{
    public class LoginApiClient : HttpClientApi, ILoginApiClient
    {
        private const string AccessTokenSessionKey = "AccessToken";

        public LoginApiClient(IOptions<ServiceEndpointsOptions> options, IHttpContextAccessor httpContextAccessor)
            : base(options.Value.Identity.BaseUrl.TrimEnd('/') + "/Login", ResolveAccessToken(httpContextAccessor))
        {
        }

        private static string ResolveAccessToken(IHttpContextAccessor httpContextAccessor)
        {
            var token = httpContextAccessor?.HttpContext?.Session?.GetString(AccessTokenSessionKey);
            return string.IsNullOrWhiteSpace(token) ? string.Empty : token.Trim();
        }
    }
}
