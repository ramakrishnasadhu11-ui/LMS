using AutoMapper;
using Lamar;
using LMS.Master.BusinessSerive.Data;
using LMS.Master.BusinessSerive.Mapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using System;
using System.IO;
using System.Reflection;
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
            services.AddControllers()
                    .AddNewtonsoftJson();
            services.AddDbContext<MasterDbContext>(options =>
                options.UseSqlServer(Configuration.GetConnectionString("MasterDb")));
            LamarConfig.RegisterContainer(services);

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
                    new OpenApiInfo
                    {
                        Title = "LMS Master API",
                        Description = "LMS Master API"
                    });

                if (File.Exists(xmlPath))
                {
                    c.IncludeXmlComments(xmlPath);
                }

                if (File.Exists(xmlPathModel) && !string.Equals(xmlPath, xmlPathModel, StringComparison.OrdinalIgnoreCase))
                {
                    c.IncludeXmlComments(xmlPathModel);
                }
            });
            var mappingConfig = new MapperConfiguration(mc =>
            {
                mc.AddProfile(new MapperProfile());
            });
            IMapper mapper = mappingConfig.CreateMapper();
            services.AddSingleton(mapper);

            services.AddCors(c =>
            {
                var allowed = Configuration["Cors:AllowedOrigins"];
                c.AddPolicy("AllowUI", options =>
                {
                    if (string.IsNullOrWhiteSpace(allowed) || allowed == "*")
                    {
                        options.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                    }
                    else
                    {
                        options.WithOrigins(allowed.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                               .AllowAnyHeader()
                               .AllowAnyMethod();
                    }
                });
            });
        }
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
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
            app.UseRouting();
            app.UseCors("AllowUI");
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
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
