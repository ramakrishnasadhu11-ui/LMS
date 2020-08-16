using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lamar;
using LMS.Master.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LMS.Master.WebApi
{
        public class LamarConfig
       {
           public static void RegisterContainer(ServiceRegistry lamarRegistryContainer)
        {
         //   lamarRegistryContainer.AddTransient<ILoginService, LoginService>();

            lamarRegistryContainer.AddScoped<IHttpContextAccessor, HttpContextAccessor>();
            lamarRegistryContainer
                 .ForConcreteType<MasterController>().Configure
                 .Scoped()
                    // .Ctor<ILoginService>().Is<LoginService>()
                     .Transient();

        }
    }
}
