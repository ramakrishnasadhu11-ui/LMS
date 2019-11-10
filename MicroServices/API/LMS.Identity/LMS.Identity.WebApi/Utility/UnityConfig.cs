using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
namespace LMS.Identity.WebApi.Utility
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
            //services.AddTransient<ICommonDataService, CommonDataService>();
            //services.AddTransient<IProposalService, ProposalService>();
            //services.AddTransient<IMarketService, MarketService>();
            //services.AddTransient<IUserChannelService, UserChannelService>();
            //services.AddTransient<IRatingAPIClient, RatingAPIClient>();  // Ratings API Client 
            //services.AddTransient<IHttpClientApi>(s => new HttpClientApi("https://67a55d8c.ngrok.io/xgratingsqa"));
           // services.AddTransient<IHttpClientApi>(s => new HttpClientApi(configuration.GetValue<string>("RatingApiUrl")));

            return services;
        }

        /// <summary>
        /// Register DB Context for the Project in this Extension Method.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="connectionString">Connection String for the Main Moduel DB</param>
        /// <returns></returns>
        public static IServiceCollection RegisterDatabaseContext(this IServiceCollection services, string connectionString)
        {
           // services.AddDbContext<ProposalDbContext>(options =>
           // {
           //     options.UseSqlServer(connectionString);
           // })
           //.AddUnitOfWork<ProposalDbContext>();
            return services;
        }
    }

}
