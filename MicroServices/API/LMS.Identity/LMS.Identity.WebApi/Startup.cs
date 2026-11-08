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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using LMS.Core.Repository.UnitOfWork;
using Microsoft.OpenApi.Models;
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LMS.Identity.BusinessSerive.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
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

             var razorpaySection = Configuration.GetSection(nameof(RazorpayOptions));
             services.Configure<RazorpayOptions>(razorpaySection);
             services.AddSingleton<IRazorpayOptions>(sp =>
                sp.GetRequiredService<IOptions<RazorpayOptions>>().Value);

             ValidateRazorpayOptions(razorpaySection.Get<RazorpayOptions>());

            // Register logging for LoginService via DI so ILogger<LoginService> can be injected
            services.AddLogging();

            var issuer = Configuration["Jwt:Issuer"]?.Trim();
            var audience = Configuration["Jwt:Audience"]?.Trim();
            var signingKey = Configuration["Jwt:SigningKey"];

            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = issuer,
                        ValidAudience = audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? string.Empty)),
                        RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                        NameClaimType = System.Security.Claims.ClaimTypes.Name
                    };
                });

            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });

           
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

                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Name = "Authorization",
                    Description = "JWT Authorization header using the Bearer scheme."
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
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
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            LogRazorpayConfigurationDiagnostics(env, logger);

            var configuredIssuer = GetJwtValue("Issuer")?.Trim();
            var configuredAudience = GetJwtValue("Audience")?.Trim();
            var configuredSigningKey = GetJwtValue("SigningKey");

            logger?.LogInformation(
                "JWT configuration loaded. Environment={Environment}, Issuer={Issuer}, Audience={Audience}, IssuerConfigured={IssuerConfigured}, AudienceConfigured={AudienceConfigured}, SigningKeyLength={SigningKeyLength}, SigningKeyFingerprint={SigningKeyFingerprint}",
                env.EnvironmentName,
                configuredIssuer,
                configuredAudience,
                !string.IsNullOrWhiteSpace(configuredIssuer),
                !string.IsNullOrWhiteSpace(configuredAudience),
                configuredSigningKey?.Length ?? 0,
                GetKeyFingerprint(configuredSigningKey));

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
                var connectionString = Configuration.GetConnectionString("IdentityDb");

                var migrationDecision = DatabaseInitializationSafetyPolicy.EvaluateStartupMigration(Configuration, env, connectionString);
                if (migrationDecision.IsAllowed)
                {
                    logger.LogInformation(
                        "Automatic startup migration is enabled for Development. DataSource={DataSource}, Database={DatabaseName}",
                        migrationDecision.DataSource,
                        migrationDecision.DatabaseName);

                    try
                    {
                        dbContext.Database.Migrate();
                    }
                    catch (Exception ex)
                    {
                        var sqlEx = TryGetSqlException(ex);
                        if (sqlEx != null)
                        {
                            logger.LogError(
                                ex,
                                "Identity startup migration failed. SqlErrorNumber={SqlErrorNumber}, SqlState={SqlState}, SqlClass={SqlClass}, Procedure={Procedure}, LineNumber={LineNumber}, Server={Server}, Message={SqlMessage}",
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
                            logger.LogError(ex, "Identity startup migration failed with non-SQL exception.");
                        }

                        throw;
                    }
                }
                else
                {
                    logger.LogWarning(
                        "Identity startup migration skipped. Reason={Reason}, DataSource={DataSource}, Database={DatabaseName}",
                        migrationDecision.Reason,
                        migrationDecision.DataSource,
                        migrationDecision.DatabaseName);
                }

                var seedingDecision = DatabaseInitializationSafetyPolicy.EvaluateStartupSeeding(Configuration, env, connectionString);
                if (seedingDecision.IsAllowed)
                {
                    try
                    {
                        SuperAdminSeeder.Seed(dbContext, Configuration);
                    }
                    catch (Exception ex)
                    {
                        var sqlEx = TryGetSqlException(ex);
                        if (sqlEx != null)
                        {
                            logger.LogError(
                                ex,
                                "Identity startup seeding failed. SqlErrorNumber={SqlErrorNumber}, SqlState={SqlState}, SqlClass={SqlClass}, Procedure={Procedure}, LineNumber={LineNumber}, Server={Server}, Message={SqlMessage}",
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
                            logger.LogError(ex, "Identity startup seeding failed with non-SQL exception.");
                        }

                        throw;
                    }
                }
                else
                {
                    logger.LogWarning(
                        "Identity startup seeding skipped. Reason={Reason}, DataSource={DataSource}, Database={DatabaseName}",
                        seedingDecision.Reason,
                        seedingDecision.DataSource,
                        seedingDecision.DatabaseName);
                }
            }

            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseCors("AllowUI");
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "LMS Identity API v1");
            });
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }

        private static string GetKeyFingerprint(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "missing";
            }

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(key.Trim()));
            return Convert.ToBase64String(hash, 0, 6);
        }

        private static void ValidateRazorpayOptions(RazorpayOptions options)
        {
            if (options == null)
            {
                throw new InvalidOperationException("RazorpayOptions configuration section is missing.");
            }

            if (!string.Equals(options.Mode, "Test", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(options.Mode, "Live", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("RazorpayOptions:Mode must be Test or Live.");
            }

            var credentials = options.GetActiveCredentials();
            if (credentials == null
                || string.IsNullOrWhiteSpace(credentials.KeyId)
                || string.IsNullOrWhiteSpace(credentials.KeySecret)
                || string.IsNullOrWhiteSpace(credentials.WebhookSecret))
            {
                throw new InvalidOperationException($"RazorpayOptions for mode '{options.Mode}' must include KeyId, KeySecret and WebhookSecret.");
            }
        }

        private void LogRazorpayConfigurationDiagnostics(IWebHostEnvironment env, ILogger<Startup> logger)
        {
            var section = Configuration.GetSection(nameof(RazorpayOptions));
            var options = section.Get<RazorpayOptions>();
            var mode = options?.Mode?.Trim();
            var active = options?.GetActiveCredentials();

            var testEnvDoubleUnderscore = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RazorpayOptions__Test__KeyId"));
            var liveEnvDoubleUnderscore = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RazorpayOptions__Live__KeyId"));
            var testEnvColon = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RazorpayOptions:Test:KeyId"));
            var liveEnvColon = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RazorpayOptions:Live:KeyId"));

            logger?.LogInformation(
                "Razorpay configuration diagnostics. Environment={Environment}, ASPNETCORE_ENVIRONMENT={AspNetCoreEnvironment}, IsProduction={IsProduction}, Mode={Mode}, ActiveKeyIdConfigured={KeyIdConfigured}, ActiveKeySecretConfigured={KeySecretConfigured}, ActiveWebhookConfigured={WebhookConfigured}, ActiveKeyIdPlaceholder={KeyIdPlaceholder}, ActiveKeySecretPlaceholder={KeySecretPlaceholder}, EnvKeyIdDetectedDoubleUnderscore={EnvKeyIdDetectedDoubleUnderscore}, EnvKeyIdDetectedColon={EnvKeyIdDetectedColon}",
                env.EnvironmentName,
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "<not-set>",
                env.IsProduction(),
                mode ?? "<missing>",
                !string.IsNullOrWhiteSpace(active?.KeyId),
                !string.IsNullOrWhiteSpace(active?.KeySecret),
                !string.IsNullOrWhiteSpace(active?.WebhookSecret),
                LooksLikePlaceholderValue(active?.KeyId),
                LooksLikePlaceholderValue(active?.KeySecret),
                testEnvDoubleUnderscore || liveEnvDoubleUnderscore,
                testEnvColon || liveEnvColon);
        }

        private static bool LooksLikePlaceholderValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim();
            return normalized.StartsWith("RAZORPAY_", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("_KEY_")
                   || normalized.Contains("_SECRET");
        }

        private static SqlException TryGetSqlException(Exception ex)
        {
            if (ex == null)
            {
                return null;
            }

            if (ex is SqlException sqlException)
            {
                return sqlException;
            }

            return TryGetSqlException(ex.InnerException);
        }

        private string GetJwtValue(string name)
        {
            var envValue = Environment.GetEnvironmentVariable($"Jwt__{name}");
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                return envValue;
            }

            return Configuration[$"Jwt:{name}"];
        }
    }
}
