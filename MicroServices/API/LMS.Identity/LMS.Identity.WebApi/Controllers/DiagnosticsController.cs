using System;
using System.Threading.Tasks;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using LMS.Identity.BusinessSerive.Services;

namespace LMS.Identity.WebApi.Controllers
{
    [ApiController]
    [Route("Diagnostics")]
    public class DiagnosticsController : ControllerBase
    {
        private readonly IMailConfiguration _mailConfiguration;
        private readonly ILogger<DiagnosticsController> _logger;

        public DiagnosticsController(IMailConfiguration mailConfiguration, ILogger<DiagnosticsController> logger)
        {
            _mailConfiguration = mailConfiguration ?? throw new ArgumentNullException(nameof(mailConfiguration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Tests SMTP connectivity and authentication using configured MailConfiguration.
        /// Useful to validate username/password, host and port.
        /// </summary>
        [HttpGet("SmtpTest")]
        public async Task<ActionResult> SmtpTest()
        {
            try
            {
                var host = string.IsNullOrWhiteSpace(_mailConfiguration.SmtpServer) ? "smtp.gmail.com" : _mailConfiguration.SmtpServer;
                var port = _mailConfiguration.Port > 0 ? _mailConfiguration.Port : 465;
                var user = _mailConfiguration.Username;
                var pass = _mailConfiguration.Password;

                var secureOption = port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

                using var client = new MailKit.Net.Smtp.SmtpClient();
                // Short timeout to avoid long waits in diagnostics
                client.Timeout = 10_000; // 10s

                await client.ConnectAsync(host, port, secureOption);

                if (!string.IsNullOrWhiteSpace(user) || !string.IsNullOrWhiteSpace(pass))
                {
                    await client.AuthenticateAsync(user, pass);
                }

                await client.DisconnectAsync(true);

                return Ok(new { success = true, message = $"SMTP connection to {host}:{port} succeeded." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMTP diagnostics failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
