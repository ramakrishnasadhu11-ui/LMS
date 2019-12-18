using AutoMapper;
using LMS.Master.BusinessSerive.Mapper;
using LMS.Master.WebApi.Utility;
using LMS.SwaggerUI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Swagger;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;
namespace LMS.Master.WebApi
{
    public class Startup
    {
        private IHostingEnvironment environment;
        public IConfiguration Configuration { get; }
        public Startup(IConfiguration configuration, IHostingEnvironment _environment)
        {
            environment = _environment;
            IConfigurationBuilder builder = new ConfigurationBuilder()
           .SetBasePath(environment.ContentRootPath)
           .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
           .AddJsonFile($"appsettings.{environment.EnvironmentName}.json", optional: true)
           .AddEnvironmentVariables();
            Configuration = builder.Build();
        }
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
            services.RegisterServices();
            services.AddAutoMapper();
            services.AddCors();
            var mapperconfig = new MapperConfiguration(op =>
            {
                op.AddProfile<MapperProfile>();
            });
            IMapper mapper = mapperconfig.CreateMapper();
            services.AddSingleton(mapper);
            services.RegisterDatabaseContext(Configuration.GetConnectionString("ModuleDB"));
            SwaggerAPIMetaData metaData = new SwaggerAPIMetaData
            {
                Name = "v1",
                SwaggerInfo = new Info { Title = "Common Data API", Version = "v1" }
            };

            services.AddSwaggerGen(swagger =>
            {
                swagger.DescribeAllEnumsAsStrings();
                swagger.DescribeAllParametersInCamelCase();
                swagger.SwaggerDoc("v1", new Swashbuckle.AspNetCore.Swagger.Info { Title = "LMS Master API", Version = "v1" });
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseCors(builder => builder
          .AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader()
         .AllowCredentials());
            app.UseMiddleware(typeof(APIResponseMiddleware));
            app.UseMvc();
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("../swagger/v1/swagger.json", "My First Swagger");
            });

            app.UseHttpsRedirection();
            app.UseMvc();

            //app.Run(async (context) =>
            //{
            //    await context.Response.WriteAsync("Hello World!");
            //});
        }
    }
}
