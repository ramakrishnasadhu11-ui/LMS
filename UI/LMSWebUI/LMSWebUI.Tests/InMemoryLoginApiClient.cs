using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using LMSWebUI.Models.Admin;
using LMSWebUI.Models;
using LMSClientFactory.Helper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMSWebUI.Tests
{
    // Minimal in-memory emulation of the Login API for store configuration endpoints used in tests.
    class InMemoryLoginApiClient : LMSWebUI.Services.ILoginApiClient
    {
        private readonly ConcurrentDictionary<string, string> _store = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private string Key(string tenant, string store) => tenant + "::" + (store ?? string.Empty);

        public Task<T> SendRequestAsync<T>(string requestUrl, RestSharp.Method method) where T : new()
        {
            return Task.FromResult(new T());
        }

        public Task<T> SendRequestAsync<T>(string requestUrl, object model, RestSharp.Method method) where T : new()
        {
            // Simulate SaveStoreConfiguration / NewStoreConfiguration when called without explicit query params
            if (string.Equals(requestUrl, "/NewStoreConfiguration", StringComparison.OrdinalIgnoreCase) || string.Equals(requestUrl, "/SaveStoreConfiguration", StringComparison.OrdinalIgnoreCase))
            {
                var json = JsonConvert.SerializeObject(model);
                try
                {
                    var j = JObject.Parse(json);
                    var cfgJson = j["ConfigurationJson"]?.ToString() ?? json;
                    var parsed = JsonConvert.DeserializeObject<StoreConfigurationViewModel>(cfgJson);
                    var key = Key(parsed.TenantName ?? string.Empty, parsed.StoreCode ?? string.Empty);
                    // store the full request wrapper (contains ConfigurationJson) so Extract can deserialize it
                    _store[key] = j.ToString();
                    var resp = new CustomerIOResponse { StatusCode = "200", Message = "ok" };
                    return Task.FromResult((T)(object)resp);
                }
                catch
                {
                    return Task.FromResult(new T());
                }
            }

            return Task.FromResult(new T());
        }

        public Task<T> SendRequestAsync<T>(string requestUrl, List<RestSharp.Parameter> parameters, RestSharp.Method method) where T : new()
        {
            return Task.FromResult(new T());
        }

        public Task<T> SendRequestAsync<T>(string requestUrl, Dictionary<string, string> queryParams, RestSharp.Method method) where T : new()
        {
            // Simulate GetStoreConfiguration by querying in-memory store
            if (string.Equals(requestUrl, "/GetStoreConfiguration", StringComparison.OrdinalIgnoreCase))
            {
                var tenant = queryParams.TryGetValue("tenantEmail", out var t) ? t : string.Empty;
                var store = queryParams.TryGetValue("storeCode", out var s) ? s : string.Empty;
                var key = Key(tenant, store);
                if (_store.TryGetValue(key, out var json))
                {
                    // Return the stored JSON as a JObject exactly as the real API would
                    try
                    {
                        var obj = JObject.Parse(json);
                        return Task.FromResult((T)(object)obj);
                    }
                    catch
                    {
                        return Task.FromResult(default(T));
                    }
                }

                return Task.FromResult(default(T));
            }

            return Task.FromResult(new T());
        }

        public Task<T> SendRequestAsync<T>(string requestUrl, object model, Dictionary<string, string> queryParams, RestSharp.Method method) where T : new()
        {
            // Simulate SaveStoreConfiguration / NewStoreConfiguration
            if (string.Equals(requestUrl, "/NewStoreConfiguration", StringComparison.OrdinalIgnoreCase) || string.Equals(requestUrl, "/SaveStoreConfiguration", StringComparison.OrdinalIgnoreCase))
            {
                var json = JsonConvert.SerializeObject(model);
                try
                {
                    var j = JObject.Parse(json);
                    var cfgJson = j["ConfigurationJson"]?.ToString() ?? json;
                    var parsed = JsonConvert.DeserializeObject<StoreConfigurationViewModel>(cfgJson);
                    var key = Key(parsed.TenantName ?? string.Empty, parsed.StoreCode ?? string.Empty);
                    // store the full request wrapper (contains ConfigurationJson) so Extract can deserialize it
                    _store[key] = j.ToString();
                    var resp = new CustomerIOResponse { StatusCode = "200", Message = "ok" };
                    return Task.FromResult((T)(object)resp);
                }
                catch
                {
                    return Task.FromResult(new T());
                }
            }

            return Task.FromResult(new T());
        }

        public Task<T> SendRequestAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, RestSharp.Method method) where T : new()
        {
            return Task.FromResult(new T());
        }

        public Task<T> SendRequestForZippedAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, RestSharp.Method method) where T : new()
        {
            return Task.FromResult(new T());
        }

        public Task<T> SendRequestAsync<T>(string requestUrl, object model, List<KeyValuePair<string, string>> queryParams, RestSharp.Method method) where T : new()
        {
            return Task.FromResult(new T());
        }

        // Helper used by tests to seed initial configurations
        public Task SaveStoreConfigurationAsync(StoreConfigurationViewModel model)
        {
            var key = Key(model.TenantName ?? string.Empty, model.StoreCode ?? string.Empty);
            var json = JsonConvert.SerializeObject(model);
            _store[key] = json;
            return Task.CompletedTask;
        }
    }
}
