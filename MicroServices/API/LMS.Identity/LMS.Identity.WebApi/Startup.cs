using AutoMapper;
using Lamar;
using LMS.Identity.BusinessSerive.Mapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Swagger;
using System;
using System.IO;
using System.Reflection;
using static LMS.Identity.BusinessSerive.Services.LoginService;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;
namespace LMS.Identity.WebApi
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
          public void ConfigureContainer(ServiceRegistry services)
        {
             services.AddMvc()
                     .SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

             LamarConfig.RegisterContainer(services);

            //var tenantRegistryOptions = Configuration.GetSection(TenantRegistryOptions.TenantRegistryConnection).Get<TenantRegistryOptions>();
            //services.AddSingleton(tenantRegistryOptions);
             services.Configure<TenantRegistryConnection>(
                          Configuration.GetSection(nameof(TenantRegistryConnection)));
             services.AddSingleton<ITenantRegistryConnection>(sp =>
                sp.GetRequiredService<IOptions<TenantRegistryConnection>>().Value);

             services.Configure<MailConfiguration>(
                          Configuration.GetSection(nameof(MailConfiguration)));
             services.AddSingleton<IMailConfiguration>(sp =>
                sp.GetRequiredService<IOptions<MailConfiguration>>().Value);

           
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            var xmlPathModel = Path.Combine(AppContext.BaseDirectory, "LMS.Identity.WebAPI.xml");
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

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
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
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "LMS Identity API v1");
                }
                else
                {
                    c.SwaggerEndpoint(Configuration["VirtualDirectory"] + "/swagger/v1/swagger.json", "LMS Identity API v1");
                }
            });
            app.UseCors(options => options.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
        }
    }
}
