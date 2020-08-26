using LMSClientFactory.Helper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LMSWebUI
{
    public static class ServiceExtensions
    {
        /// <summary>
        /// Register All the Components with this Extension Method, All the Business Services and Repositories used in this Project
        /// must be registered here for Enabling Dependency Injection
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configuration"></param>
        /// <returns>Service Collection</returns>
        public static IServiceCollection RegisterServices(
            this IServiceCollection services, IConfiguration configuration)
        {
           
            services.AddTransient<IHttpClientApi>(s => new HttpClientApi(configuration.GetValue<string>("LoginApiUrl")));
         //   services.AddTransient<IHttpClientApi>(s1 => new HttpClientApi(configuration.GetValue<string>("CustomerApiUrl")));
            return services;
        }


    }
}
