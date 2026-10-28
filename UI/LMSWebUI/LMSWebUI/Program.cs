using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LMSWebUI
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddSingleton<IStartupFilter, GlobalExceptionLoggingStartupFilter>();
                    });

                    webBuilder.UseStartup<Startup>();
                });
    }

    internal sealed class GlobalExceptionLoggingStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.UseMiddleware<GlobalExceptionLoggingMiddleware>();
                next(app);
            };
        }
    }

    internal sealed class GlobalExceptionLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _contentRootPath;

        public GlobalExceptionLoggingMiddleware(RequestDelegate next, IWebHostEnvironment env)
        {
            _next = next;
            _contentRootPath = env?.ContentRootPath ?? AppContext.BaseDirectory;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                TryWriteLog(context, ex);
                throw;
            }
        }

        private void TryWriteLog(HttpContext context, Exception ex)
        {
            try
            {
                var logsDirectory = Path.Combine(_contentRootPath, "logs");
                Directory.CreateDirectory(logsDirectory);

                var filePath = Path.Combine(logsDirectory, $"errors-{DateTime.UtcNow:yyyyMMdd}.log");
                var builder = new StringBuilder();
                builder.AppendLine($"TimestampUtc: {DateTime.UtcNow:O}");
                builder.AppendLine($"RequestPath: {context?.Request?.Path.Value}");
                builder.AppendLine($"ExceptionType: {ex.GetType().FullName}");
                builder.AppendLine($"Message: {ex.Message}");
                builder.AppendLine("StackTrace:");
                builder.AppendLine(ex.ToString());
                builder.AppendLine(new string('-', 120));

                File.AppendAllText(filePath, builder.ToString());
            }
            catch
            {
            }
        }
    }
}
