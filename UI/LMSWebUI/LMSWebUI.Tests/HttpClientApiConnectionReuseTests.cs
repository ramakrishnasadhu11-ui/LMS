using System;
using System.Linq;
using System.Reflection;
using LMSClientFactory.Helper;
using RestSharp;
using Xunit;

namespace LMSWebUI.Tests
{
    public class HttpClientApiConnectionReuseTests
    {
        private static object GetPrivateField(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return field.GetValue(instance);
        }

        [Fact]
        public void HttpClientApi_ReusesRestClient_ForSameBaseUrl()
        {
            var api1 = new HttpClientApi("https://example.org/login", "token-one");
            var api2 = new HttpClientApi("https://example.org/login", "token-two");

            var client1 = GetPrivateField(api1, "_client");
            var client2 = GetPrivateField(api2, "_client");

            Assert.Same(client1, client2);
        }

        [Fact]
        public void HttpClientApi_AppliesAuthorization_PerRequest_NotAsSharedDefaultHeader()
        {
            var apiWithTokenA = new HttpClientApi("https://example.org/login", "token-a");
            var apiWithTokenB = new HttpClientApi("https://example.org/login", "token-b");

            var applyMethod = typeof(HttpClientApi).GetMethod("ApplyAuthorizationHeader", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(applyMethod);

            var requestA = new RestRequest("TenantLogin", Method.POST);
            var requestB = new RestRequest("TenantLogin", Method.POST);

            applyMethod.Invoke(apiWithTokenA, new object[] { requestA });
            applyMethod.Invoke(apiWithTokenB, new object[] { requestB });

            var authHeaderA = requestA.Parameters.FirstOrDefault(p => p.Type == ParameterType.HttpHeader && string.Equals(p.Name, "Authorization", StringComparison.OrdinalIgnoreCase));
            var authHeaderB = requestB.Parameters.FirstOrDefault(p => p.Type == ParameterType.HttpHeader && string.Equals(p.Name, "Authorization", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(authHeaderA);
            Assert.NotNull(authHeaderB);
            Assert.Equal("Bearer token-a", authHeaderA.Value?.ToString());
            Assert.Equal("Bearer token-b", authHeaderB.Value?.ToString());

            var sharedClient = GetPrivateField(apiWithTokenA, "_client");
            var defaultParametersProperty = sharedClient.GetType().GetProperty("DefaultParameters", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(defaultParametersProperty);

            var defaultParameters = defaultParametersProperty.GetValue(sharedClient) as System.Collections.IEnumerable;
            Assert.NotNull(defaultParameters);

            var hasDefaultAuthorization = false;
            foreach (var parameter in defaultParameters)
            {
                var nameProperty = parameter.GetType().GetProperty("Name");
                var parameterName = nameProperty?.GetValue(parameter)?.ToString();
                if (string.Equals(parameterName, "Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    hasDefaultAuthorization = true;
                    break;
                }
            }

            Assert.False(hasDefaultAuthorization);
        }
    }
}
