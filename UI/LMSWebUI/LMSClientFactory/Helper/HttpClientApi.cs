using RestSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace LMSClientFactory.Helper
{
    public class HttpClientApi : IHttpClientApi
    {
        private readonly IRestClient _client;
        public HttpClientApi(string url)
        {
            _client = new RestClient(url);

        }
        public HttpClientApi(string url, string access_token)
        {
            _client = new RestClient(url);
            _client.AddDefaultHeader("Authorization", string.Format("Bearer : " + access_token));
        }
        /// <summary>
        /// Async Call
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="request"></param>
        /// <returns></returns>
        private async Task<T> SendRequest<T>(IRestRequest request) where T : new()
        {
            request.RequestFormat = DataFormat.Json;
            request.JsonSerializer = new RestSharpJsonNetSerializer();

            IRestResponse response;
            try
            {
                response = await _client.ExecuteTaskAsync(request);
            }
            catch (Exception ex) when (ex is JsonException || ex is System.Xml.XmlException)
            {
                throw new ApiUnavailableException("The API returned an invalid response format.", ex);
            }

            // RestSharp reports ResponseStatus.Error for any non-success status code, so a
            // status code of 0 is used to detect a genuine transport failure. Otherwise a
            // meaningful response such as 403 (not approved) would be hidden behind a
            // misleading "service unavailable" message.
            var hasHttpResponse = response != null && response.StatusCode != 0;

            if (!hasHttpResponse
                && response != null
                && (response.ResponseStatus == ResponseStatus.Error || response.ResponseStatus == ResponseStatus.TimedOut))
            {
                throw new ApiUnavailableException(
                    response.ErrorMessage ?? "The API could not be reached.",
                    response.ErrorException);
            }

            if (response == null)
            {
                throw new ApiUnavailableException("The API could not be reached.");
            }

            if (response.StatusCode == HttpStatusCode.InternalServerError || response.StatusCode == HttpStatusCode.GatewayTimeout)
            {
                throw new ApiUnavailableException(
                    $"The API returned {(int)response.StatusCode}.",
                    response.ErrorException,
                    (int)response.StatusCode,
                    response.Content);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // RestSharp's ErrorException only says "Request failed with status code NotFound",
                // which hides the API payload. Callers inspect that payload to tell a genuine
                // failure apart from a valid "no record yet" response, so the body wins when present.
                if (!string.IsNullOrWhiteSpace(response.Content))
                {
                    throw new Exception(response.Content, response.ErrorException);
                }

                throw response.ErrorException ?? new Exception("The API returned 404.");
            }

            if (typeof(T) == typeof(JObject))
            {
                if (!string.IsNullOrWhiteSpace(response.Content))
                {
                    try
                    {
                        var parsedToken = JToken.Parse(response.Content);
                        if (parsedToken is JObject parsedObject)
                        {
                            return (T)(object)parsedObject;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(response.Content))
            {
                try
                {
                    if (typeof(T) == typeof(string))
                    {
                        return (T)(object)response.Content;
                    }

                    var parsedData = JsonConvert.DeserializeObject<T>(response.Content);
                    if ((object)parsedData != null)
                    {
                        if (parsedData is not System.Collections.ICollection parsedCollection || parsedCollection.Count > 0)
                        {
                            return parsedData;
                        }
                    }

                    var token = JToken.Parse(response.Content);
                    var unwrappedToken = token["ResultSet"]
                        ?? token["resultSet"]
                        ?? token["Data"]
                        ?? token["data"]
                        ?? token["$values"]
                        ?? token;

                    var unwrappedData = unwrappedToken.ToObject<T>();
                    if ((object)unwrappedData != null)
                    {
                        return unwrappedData;
                    }
                }
                catch
                {
                }
            }

            return default;
        }
        /// <summary>
        /// With Out Async Call
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private string SendRequest(IRestRequest request)
        {
            try
            {
                request.RequestFormat = DataFormat.Json;

                IRestResponse response = _client.Execute(request);

                if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.InternalServerError)
                {
                    throw new Exception(response.Content);
                }

                if (response.ErrorException != null)
                {
                    throw response.ErrorException;
                }

                return response.Content;
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task<T> SendRequestAsync<T>(string requestUrl, Method method) where T : new()
        {
            try
            {
                IRestRequest restRequest = new RestRequest(requestUrl, method)
                {
                    RequestFormat = DataFormat.Json
                };
                return await SendRequest<T>(restRequest);
            }
            catch
            {
                throw;
            }
        }
        public async Task<T> SendRequestAsync<T>(string requestUrl, object model, Method method) where T : new()
        {
            try
            {
                IRestRequest restRequest = new RestRequest(requestUrl, method)
                {
                    RequestFormat = DataFormat.Json,
                    JsonSerializer = new RestSharpJsonNetSerializer()
                };
                restRequest.AddJsonBody(model);
                return await SendRequest<T>(restRequest);
            }
            catch
            {
                throw;
            }
        }
        public async Task<T> SendRequestAsync<T>(string requestUrl, List<Parameter> parameters, Method method) where T : new()
        {
            try
            {
                IRestRequest restRequest = new RestRequest(requestUrl, method);
                restRequest.Parameters.AddRange(parameters);
                return await SendRequest<T>(restRequest);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        public async Task<T> SendRequestAsync<T>(string requestUrl, Dictionary<string, string> queryParams, Method method) where T : new()
        {
            try
            {
                IRestRequest restRequest = new RestRequest(requestUrl, method);
                foreach (KeyValuePair<string, string> param in queryParams)
                {
                    restRequest.AddQueryParameter(param.Key, param.Value);
                }

                return await SendRequest<T>(restRequest);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        public async Task<T> SendRequestAsync<T>(string requestUrl, object model, Dictionary<string, string> queryParams, Method method) where T : new()
        {
            try
            {
                IRestRequest restRequest = new RestRequest(requestUrl, method);
                foreach (KeyValuePair<string, string> param in queryParams)
                {
                    restRequest.AddQueryParameter(param.Key, param.Value);
                }
                restRequest.AddJsonBody(model);

                return await SendRequest<T>(restRequest);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<T> SendRequestAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
        {
            try
            {
                IRestRequest restRequest = new RestRequest(requestUrl, method);
                foreach (KeyValuePair<string, string> param in queryParams)
                {
                    restRequest.AddQueryParameter(param.Key, param.Value);
                }

                return await SendRequest<T>(restRequest);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<T> SendRequestForZippedAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
        {
            IRestRequest restRequest = new RestRequest(requestUrl, method);
            foreach (KeyValuePair<string, string> param in queryParams)
            {
                restRequest.AddQueryParameter(param.Key, param.Value);
            }

            //string zippedData;

            return await SendRequest<T>(restRequest);
        }

        public async Task<T> SendRequestAsync<T>(string requestUrl, object model, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
        {
            try
            {
                IRestRequest restRequest = new RestRequest(requestUrl, method);
                foreach (KeyValuePair<string, string> param in queryParams)
                {
                    restRequest.AddQueryParameter(param.Key, param.Value);
                }

                restRequest.AddJsonBody(model);

                return await SendRequest<T>(restRequest);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
