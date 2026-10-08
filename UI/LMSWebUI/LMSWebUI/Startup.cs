using System;
using LMSWebUI.Data;
using LMSWebUI.Helpers;
using LMSWebUI.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LMSWebUI
{
    public class Startup
    {
<<<<<<< Updated upstream
        public string ApiUrl { get; set; }
        public Startup(IConfiguration configuration, IHostingEnvironment environment)
=======
        public Startup(IConfiguration configuration)
>>>>>>> Stashed changes
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }
<<<<<<< Updated upstream
        public IHostingEnvironment Environment { get; }
=======

>>>>>>> Stashed changes
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.RegisterServices(Configuration);
            // Add EF Core + Identity
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")));

            services.AddIdentity<ApplicationUser, IdentityRole>(options =>
                {
                    options.Password.RequireDigit = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireUppercase = true;
                    options.Password.RequiredLength = 8;
                    options.Password.RequireNonAlphanumeric = true;
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();
            services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod());
            });

            services.Configure<IISOptions>(options =>
            {
                options.AutomaticAuthentication = false;
            });

            services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(60);
            });

<<<<<<< Updated upstream
            services.AddSession(options => 
            {  
               options.IdleTimeout = TimeSpan.FromMinutes(60);//You can set Time   
           });  
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
=======
            services.AddControllersWithViews();
            // Register IHttpClientFactory for calling other microservices
            services.AddHttpClient();
>>>>>>> Stashed changes
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
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseStaticFiles();
            app.UseCookiePolicy();
<<<<<<< Updated upstream
=======
            app.UseRouting();
            app.UseCors("AllowAll");
            app.UseAuthentication();
            app.UseAuthorization();
>>>>>>> Stashed changes
            app.UseSession();
            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Login}/{action=Login}/{id?}");
            });

            // Seed database with SuperAdmin user/role
            try
            {
                using (var scope = app.ApplicationServices.CreateScope())
                {
                    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    // Try applying migrations; if migrations are not present, fall back to EnsureCreated
                    try
                    {
                        db.Database.Migrate();
                    }
                    catch
                    {
                        db.Database.EnsureCreated();
                    }
                    DbInitializer.SeedAsync(userManager, roleManager).GetAwaiter().GetResult();
                }
            }
            catch
            {
                // swallow exceptions during seeding to avoid breaking startup; inspect logs in real scenarios
            }
        }
    }
}
