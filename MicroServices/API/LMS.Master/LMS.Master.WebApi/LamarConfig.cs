using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lamar;
using LMS.Master.BusinessSerive.Interfaces;
using LMS.Master.BusinessSerive.Services;
using LMS.Master.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LMS.Master.WebApi
{
        public class LamarConfig
       {
           public static void RegisterContainer(ServiceRegistry lamarRegistryContainer)
        {
            lamarRegistryContainer.AddTransient<ICustomer, Customerservice>();

            lamarRegistryContainer.AddScoped<IHttpContextAccessor, HttpContextAccessor>();
            lamarRegistryContainer
                 .ForConcreteType<CustomerController>().Configure
                 .Scoped()
                     .Ctor<ICustomer>().Is<Customerservice>()
                     .Transient();

        }
    }
}
