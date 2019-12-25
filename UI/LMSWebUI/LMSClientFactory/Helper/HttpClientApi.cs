using RestSharp;
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
            try
            {
                request.RequestFormat = DataFormat.Json;
                request.JsonSerializer = new RestSharpJsonNetSerializer();
                TaskCompletionSource<T> taskCompletionSource = new TaskCompletionSource<T>();
                _client.ExecuteAsync<T>(request, (response) =>
                {
                    if (response.ResponseStatus == ResponseStatus.Error || response.ResponseStatus == ResponseStatus.TimedOut)
                    {
                        taskCompletionSource.SetException(response.ErrorException);
                    }
                    else if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.InternalServerError || response.StatusCode == HttpStatusCode.GatewayTimeout)
                    {
                        taskCompletionSource.SetException(response.ErrorException);
                    }
                    else
                    {
                        taskCompletionSource.SetResult(response.Data);
                    }
                });
                return await taskCompletionSource.Task;
            }
            catch (Exception ex)
            {
                throw ex;
            }
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
            catch (Exception ex)
            {
                throw ex;
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
            catch (Exception ex)
            {
                throw ex;
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
