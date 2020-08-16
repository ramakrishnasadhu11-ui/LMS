using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lamar.Microsoft.DependencyInjection;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LMS.Master.WebApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
          CreateWebHostBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                })
                 //.UseNLog()
                 .Build().Run();
        }
         public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                 .UseLamar()
                .UseStartup<Startup>();
    }
}
