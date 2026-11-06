using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Data.SqlClient;
using LMSWebUI.Data;
using LMSWebUI.Helpers;
using LMSWebUI.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LMSWebUI
{
    public class Startup
    {
        private const string DefaultDataProtectionApplicationName = "ELSUI";
        private readonly IWebHostEnvironment _environment;
        private string _dataProtectionApplicationName = DefaultDataProtectionApplicationName;
        private string _dataProtectionKeysPath = string.Empty;
        private bool _dataProtectionDpapiEnabled;
        private bool _dataProtectionFallbackUsed;
        private string _dataProtectionResolution = string.Empty;

        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            Configuration = configuration;
            _environment = environment;
        }

        public IConfiguration Configuration { get; }
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            var configuredApplicationName = Configuration["DataProtection:ApplicationName"];
            var dataProtectionApplicationName = string.IsNullOrWhiteSpace(configuredApplicationName)
                ? DefaultDataProtectionApplicationName
                : configuredApplicationName.Trim();

            var configuredKeysPath = Configuration["DataProtection:KeysPath"];
            var dataProtectionKeysPath = ResolveWritableDataProtectionKeysPath(
                configuredKeysPath,
                dataProtectionApplicationName,
                out var pathResolution,
                out var usedFallbackPath);

            var enableWindowsDpapi = Configuration.GetValue<bool?>("DataProtection:ProtectKeysWithDpapi") == true;

            var dataProtectionBuilder = services
                .AddDataProtection()
                .SetApplicationName(dataProtectionApplicationName)
                .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && enableWindowsDpapi)
            {
                dataProtectionBuilder.ProtectKeysWithDpapi();
            }

            _dataProtectionApplicationName = dataProtectionApplicationName;
            _dataProtectionKeysPath = dataProtectionKeysPath;
            _dataProtectionDpapiEnabled = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && enableWindowsDpapi;
            _dataProtectionFallbackUsed = usedFallbackPath;
            _dataProtectionResolution = pathResolution;

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

            services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = "/Login/Login";
                options.AccessDeniedPath = "/Login/Login";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
            });

            services.AddControllersWithViews()
                .AddNewtonsoftJson();
            services.AddHttpClient();
            services.AddHttpContextAccessor();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            logger.LogInformation(
                "DataProtection initialized. ApplicationName={ApplicationName}; KeysPath={KeysPath}; DpapiEnabled={DpapiEnabled}; FallbackPathUsed={FallbackPathUsed}; ContentRoot={ContentRoot}; Environment={EnvironmentName}; Resolution={Resolution}",
                _dataProtectionApplicationName,
                _dataProtectionKeysPath,
                _dataProtectionDpapiEnabled,
                _dataProtectionFallbackUsed,
                env.ContentRootPath,
                env.EnvironmentName,
                _dataProtectionResolution);

            if (_dataProtectionFallbackUsed)
            {
                logger.LogWarning("Configured DataProtection key path was not writable. Fallback writable path was selected: {KeysPath}", _dataProtectionKeysPath);
            }

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
            app.UseRouting();
            app.UseCors("AllowAll");
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseSession();
            app.Use(async (context, next) =>
            {
                if (context.User?.Identity?.IsAuthenticated == true
                    && string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase)
                    && !Path.HasExtension(context.Request.Path.Value ?? string.Empty))
                {
                    context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                    context.Response.Headers["Pragma"] = "no-cache";
                    context.Response.Headers["Expires"] = "0";
                }

                await next();
            });
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Login}/{action=Login}/{id?}");
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
                    catch (Exception ex)
                    {
                        var sqlEx = TryGetSqlException(ex);
                        if (sqlEx != null)
                        {
                            logger.LogError(
                                ex,
                                "ApplicationDb migration failed. SqlErrorNumber={SqlErrorNumber}, SqlState={SqlState}, SqlClass={SqlClass}, Procedure={Procedure}, LineNumber={LineNumber}, Server={Server}, Message={SqlMessage}",
                                sqlEx.Number,
                                sqlEx.State,
                                sqlEx.Class,
                                sqlEx.Procedure ?? string.Empty,
                                sqlEx.LineNumber,
                                sqlEx.Server ?? string.Empty,
                                sqlEx.Message);
                        }
                        else
                        {
                            logger.LogError(ex, "ApplicationDb migration failed with non-SQL exception. Falling back to EnsureCreated.");
                        }

                        try
                        {
                            db.Database.EnsureCreated();
                        }
                        catch (Exception ensureCreatedEx)
                        {
                            var ensureCreatedSqlEx = TryGetSqlException(ensureCreatedEx);
                            if (ensureCreatedSqlEx != null)
                            {
                                logger.LogError(
                                    ensureCreatedEx,
                                    "ApplicationDb EnsureCreated failed after migration failure. SqlErrorNumber={SqlErrorNumber}, SqlState={SqlState}, SqlClass={SqlClass}, Procedure={Procedure}, LineNumber={LineNumber}, Server={Server}, Message={SqlMessage}",
                                    ensureCreatedSqlEx.Number,
                                    ensureCreatedSqlEx.State,
                                    ensureCreatedSqlEx.Class,
                                    ensureCreatedSqlEx.Procedure ?? string.Empty,
                                    ensureCreatedSqlEx.LineNumber,
                                    ensureCreatedSqlEx.Server ?? string.Empty,
                                    ensureCreatedSqlEx.Message);
                            }
                            else
                            {
                                logger.LogError(ensureCreatedEx, "ApplicationDb EnsureCreated failed with non-SQL exception.");
                            }

                            throw;
                        }
                    }

                    DbInitializer.SeedAsync(userManager, roleManager).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                var sqlEx = TryGetSqlException(ex);
                if (sqlEx != null)
                {
                    logger.LogError(
                        ex,
                        "Startup DB seed/init failed. SqlErrorNumber={SqlErrorNumber}, SqlState={SqlState}, SqlClass={SqlClass}, Procedure={Procedure}, LineNumber={LineNumber}, Server={Server}, Message={SqlMessage}",
                        sqlEx.Number,
                        sqlEx.State,
                        sqlEx.Class,
                        sqlEx.Procedure ?? string.Empty,
                        sqlEx.LineNumber,
                        sqlEx.Server ?? string.Empty,
                        sqlEx.Message);
                }
                else
                {
                    logger.LogError(ex, "Startup DB seed/init failed with non-SQL exception.");
                }

                // Preserve existing behavior: startup continues even when seeding/init fails.
            }
        }

        private static SqlException TryGetSqlException(Exception ex)
        {
            if (ex == null)
            {
                return null;
            }

            if (ex is SqlException direct)
            {
                return direct;
            }

            return ex.InnerException as SqlException;
        }

        private string ResolveWritableDataProtectionKeysPath(string configuredKeysPath, string applicationName, out string resolution, out bool usedFallback)
        {
            var contentRoot = _environment?.ContentRootPath;
            if (string.IsNullOrWhiteSpace(contentRoot))
            {
                contentRoot = AppContext.BaseDirectory;
            }

            string ToAbsolutePath(string path)
            {
                var normalized = path.Trim().Replace('/', Path.DirectorySeparatorChar);
                return Path.IsPathRooted(normalized)
                    ? normalized
                    : Path.Combine(contentRoot, normalized);
            }

            var candidates = new List<(string Path, string Source)>();

            if (!string.IsNullOrWhiteSpace(configuredKeysPath))
            {
                candidates.Add((ToAbsolutePath(configuredKeysPath), "configured"));
            }

            candidates.Add((Path.Combine(contentRoot, "App_Data", "DataProtection-Keys", applicationName), "content-root-default"));

            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDir))
            {
                candidates.Add((Path.Combine(baseDir, "App_Data", "DataProtection-Keys", applicationName), "base-directory-default"));
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                candidates.Add((Path.Combine(localAppData, applicationName, "DataProtection-Keys"), "local-app-data-fallback"));
            }

            var distinctCandidates = candidates
                .Where(c => !string.IsNullOrWhiteSpace(c.Path))
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var failures = new List<string>();
            for (var i = 0; i < distinctCandidates.Count; i++)
            {
                var candidate = distinctCandidates[i];
                if (TryEnsureWritableDirectory(candidate.Path, out var failureReason))
                {
                    usedFallback = i > 0;
                    resolution = $"selected={candidate.Path};source={candidate.Source}";
                    return candidate.Path;
                }

                failures.Add($"{candidate.Source}:{candidate.Path} => {failureReason}");
            }

            usedFallback = false;
            resolution = string.Join(" | ", failures);
            throw new InvalidOperationException($"Unable to initialize Data Protection key directory. {resolution}");
        }

        private static bool TryEnsureWritableDirectory(string path, out string reason)
        {
            try
            {
                Directory.CreateDirectory(path);
                var probeFilePath = Path.Combine(path, $".dp-write-test-{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probeFilePath, DateTime.UtcNow.ToString("O"));
                File.Delete(probeFilePath);
                reason = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }
    }
}
