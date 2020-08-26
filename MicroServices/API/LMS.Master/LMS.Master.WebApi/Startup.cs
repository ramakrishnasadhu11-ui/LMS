using AutoMapper;
using Lamar;
using LMS.Master.BusinessSerive.Mapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Swagger;
using System;
using System.IO;
using System.Reflection;
using static LMS.Master.BusinessSerive.Services.Customerservice;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;
namespace LMS.Master.WebApi
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        public Startup(IConfiguration configuration)
        {
             Configuration = configuration;
        }
          public void ConfigureContainer(ServiceRegistry services)
        {
             services.AddMvc()
                     .SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
            LamarConfig.RegisterContainer(services);
            services.Configure<TenantCustomerRegistryConnection>(
                         Configuration.GetSection(nameof(TenantCustomerRegistryConnection)));
            services.AddSingleton<ITenantCustomerRegistryConnection>(sp =>
               sp.GetRequiredService<IOptions<TenantCustomerRegistryConnection>>().Value);

            //services.Configure<MailConfiguration>(
            //             Configuration.GetSection(nameof(MailConfiguration)));
            //services.AddSingleton<IMailConfiguration>(sp =>
            //   sp.GetRequiredService<IOptions<MailConfiguration>>().Value);


            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            var xmlPathModel = Path.Combine(AppContext.BaseDirectory, "LMS.Master.WebApi.xml");
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1",
                    new Info
                    {
                        Title = "LMS Identity API",
                        Description = "LMS Identity API"
                    });
                c.IncludeXmlComments(xmlPath);
                c.IncludeXmlComments(xmlPathModel);
            });
            var mappingConfig = new MapperConfiguration(mc =>
            {
                mc.AddProfile(new MapperProfile());
            });
            IMapper mapper = mappingConfig.CreateMapper();
            services.AddSingleton(mapper);

             services.AddCors(c =>
            {
                c.AddPolicy("AllowOrigin", options => options.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
            });
        }
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                 app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("../swagger/v1/swagger.json", "My First Swagger");
            });
            app.UseHttpsRedirection();
            app.UseMvc();
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                if (env.IsDevelopment())
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "LMS Master Api v1");
                }
                else
                {
                    c.SwaggerEndpoint(Configuration["VirtualDirectory"] + "/swagger/v1/swagger.json", "LMS Master Api v1");
                }
            });
            app.UseCors(options => options.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
        }
    }
}
