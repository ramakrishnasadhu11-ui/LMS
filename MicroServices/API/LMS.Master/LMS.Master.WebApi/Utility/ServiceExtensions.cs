using LMS.Core.Repository.UnitOfWork;
using LMS.Master.BusinessSerive.Interfaces;
using LMS.Master.BusinessSerive.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LMS.Master.WebApi.Utility
{
    public static class ServiceExtensions
    {
        /// <summary>
        /// Register All the Components with this Extension Method, All the Business Services and Repositories used in this Project
        /// must be registered here for Enabling Dependency Injection
        /// </summary>
        /// <param name="services"></param>
        /// <returns>Service Collection</returns>
        public static IServiceCollection RegisterServices(
            this IServiceCollection services)
        {
            services.AddTransient<ILaundryServicesService, LaundryServicesService>();
            //services.AddTransient<ICopyItemsService, CopyItemsService>();
            //services.AddTransient<IOrders, OrderService>();
            //services.AddTransient<IOrderLine, OrderLineServiceService>();
            //services.AddTransient<ISettings, SystemsSettingsService>();
            //services.AddTransient<ISpotsService, SpotsService>();
            //services.AddTransient<ILibraryService, LibraryService>();

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
           // services.AddDbContext<LMS_Master_DevContext>(options =>
           // {
           //     options.UseSqlServer(connectionString);
           // })
           //.AddUnitOfWork<LMS_Master_DevContext>();
            return services;
        }
    }
}
