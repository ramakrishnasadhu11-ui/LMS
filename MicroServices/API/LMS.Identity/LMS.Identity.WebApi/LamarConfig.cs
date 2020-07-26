using Lamar;
using LMS.Identity.BusinessSerive.Interfaces;
using LMS.Identity.BusinessSerive.Services;
using LMS.Identity.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LMS.Identity.WebApi
{
    public class LamarConfig
    {
           public static void RegisterContainer(ServiceRegistry lamarRegistryContainer)
        {
            lamarRegistryContainer.AddTransient<ILoginService, LoginService>();

            lamarRegistryContainer.AddScoped<IHttpContextAccessor, HttpContextAccessor>();
            lamarRegistryContainer
                 .ForConcreteType<LoginController>().Configure
                 .Scoped()
                     .Ctor<ILoginService>().Is<LoginService>()
                     .Transient();

        }
    }
}
