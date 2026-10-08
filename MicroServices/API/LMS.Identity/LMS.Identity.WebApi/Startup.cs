using AutoMapper;
using Lamar;
using LMS.Identity.BusinessSerive.Data;
using LMS.Identity.BusinessSerive.Mapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using LMS.Core.Repository.UnitOfWork;
using Microsoft.OpenApi.Models;
using System;
using System.IO;
using System.Reflection;
using LMS.Identity.BusinessSerive.Services;
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
             services.AddControllers();

             services.AddDbContext<IdentityDbContext>(options =>
                 options.UseSqlServer(Configuration.GetConnectionString("IdentityDb")));
            // Register unit of work for IdentityDbContext so services can depend on IUnitOfWork<IdentityDbContext>
            services.AddUnitOfWork<IdentityDbContext>();

             LamarConfig.RegisterContainer(services);

             services.Configure<MailConfiguration>(
                          Configuration.GetSection(nameof(MailConfiguration)));
             services.AddSingleton<IMailConfiguration>(sp =>
                sp.GetRequiredService<IOptions<MailConfiguration>>().Value);

            // Register logging for LoginService via DI so ILogger<LoginService> can be injected
            services.AddLogging();

           
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            var xmlPathModel = Path.Combine(AppContext.BaseDirectory, "LMS.Identity.WebAPI.xml");
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1",
                    new OpenApiInfo
                    {
                        Title = "LMS Identity API",
                        Description = "LMS Identity API"
                    });
                if (File.Exists(xmlPath))
                {
                    c.IncludeXmlComments(xmlPath);
                }

                if (File.Exists(xmlPathModel))
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

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
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

            using (var scope = app.ApplicationServices.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                // Do not fall back to EnsureCreated: it does not apply migrations to
                // an existing database. Let migration failures stop startup visibly.
                dbContext.Database.Migrate();

                SuperAdminSeeder.Seed(dbContext, Configuration);
            }

            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseCors("AllowUI");
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
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }
    }
}
