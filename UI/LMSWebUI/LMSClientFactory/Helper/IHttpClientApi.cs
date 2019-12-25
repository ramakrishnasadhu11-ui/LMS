using RestSharp;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LMSClientFactory.Helper
{
    public interface IHttpClientApi
    {/// <summary>
     /// With Requested URL and Methods Like (Get,Post,Put,DELETE, HEAD,OPTIONS ,PATCH,MERGE, COPY
     /// </summary>
     /// <typeparam name="T"></typeparam>
     /// <param name="requestUrl"></param>
     /// <param name="method"></param>
     /// <returns></returns>
        Task<T> SendRequestAsync<T>(string requestUrl, Method method) where T : new();
        /// <summary>
        /// With Request URL , Model to Post or formBody Request,Method Type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="requestUrl"></param>
        /// <param name="model"></param>
        /// <param name="method"></param>
        /// <returns></returns>
        Task<T> SendRequestAsync<T>(string requestUrl, object model, Method method) where T : new();
        /// <summary>
        /// With Request URL,Parameters ,Method Type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="requestUrl"></param>
        /// <param name="parameters"></param>
        /// <param name="method"></param>
        /// <returns></returns>
        Task<T> SendRequestAsync<T>(string requestUrl, List<Parameter> parameters, Method method) where T : new();
        /// <summary>
        /// Query String
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="requestUrl"></param>
        /// <param name="queryParams"></param>
        /// <param name="method"></param>
        /// <returns></returns>
        Task<T> SendRequestAsync<T>(string requestUrl, Dictionary<string, string> queryParams, Method method) where T : new();
        /// <summary>
        /// With Query string and Post Model
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="requestUrl"></param>
        /// <param name="model"></param>
        /// <param name="queryParams"></param>
        /// <param name="method"></param>
        /// <returns></returns>
        Task<T> SendRequestAsync<T>(string requestUrl, object model, Dictionary<string, string> queryParams, Method method) where T : new();
        /// <summary>
        /// With Request URL,Parameters ,Method Type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="requestUrl"></param>
        /// <param name="queryParams"></param>
        /// <param name="method"></param>
        /// <returns></returns>
        Task<T> SendRequestAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new();

        Task<T> SendRequestForZippedAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new();

        Task<T> SendRequestAsync<T>(string requestUrl, object model, List<KeyValuePair<string, string>> queryParams, Method method) where T : new();
    }
}
