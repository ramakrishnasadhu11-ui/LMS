using AutoMapper;
using Lamar;
using LMS.Master.BusinessSerive.Data;
using LMS.Master.BusinessSerive.Mapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
<<<<<<< Updated upstream
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Swagger;
=======
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
>>>>>>> Stashed changes
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
<<<<<<< Updated upstream
             services.AddMvc()
                     .SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
=======
             services.AddControllers();
            services.AddDbContext<MasterDbContext>(options =>
                options.UseSqlServer(Configuration.GetConnectionString("MasterDb")));
>>>>>>> Stashed changes
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
                    new Info
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
<<<<<<< Updated upstream
            app.UseSwaggerUI(c =>
=======
            app.UseRouting();
            app.UseSwagger();
            app.UseCors("AllowUI");

            app.UseEndpoints(endpoints =>
>>>>>>> Stashed changes
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
