using LMS.Identity.BusinessSerive.Data;
using LMS.Identity.BusinessSerive.Interfaces;
using LMS.Core.Repository.UnitOfWork;
using MailKit.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LMS.Identity.BusinessSerive.Services
{
    public class TenantEmailService : ITenantEmailService
    {
        private readonly IUnitOfWork<IdentityDbContext> _unitOfWork;
        private readonly IMailConfiguration _mailConfiguration;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TenantEmailService> _logger;

        public TenantEmailService(
            IUnitOfWork<IdentityDbContext> unitOfWork,
            IMailConfiguration mailConfiguration,
            IConfiguration configuration,
            ILogger<TenantEmailService> logger)
        {
            _unitOfWork = unitOfWork;
            _mailConfiguration = mailConfiguration;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendMailAsync(string email, string subject, string body, string tenantId = null, string tenantEmail = null)
        {
            var effectiveMail = await ResolveTenantMailConfigurationAsync(tenantId, tenantEmail);
            var userName = effectiveMail.Username;
            var password = effectiveMail.Password;
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("MailConfiguration:Username/Password are not configured. Set them via user-secrets or the MailConfiguration__Username and MailConfiguration__Password environment variables.");
            }

            var host = string.IsNullOrWhiteSpace(effectiveMail.Host) ? "smtp.gmail.com" : effectiveMail.Host;
            var port = effectiveMail.Port > 0 ? effectiveMail.Port : 587;

            var fromAddress = string.IsNullOrWhiteSpace(effectiveMail.FromAddress) ? userName : effectiveMail.FromAddress;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Excel Laundry Services", fromAddress));
            message.To.Add(new MailboxAddress("ELS Tenant", email));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = EnsureStandardSupportFooter(body) }.ToMessageBody();

            var enableSsl = effectiveMail.EnableSsl ?? (port == 465 || port == 587);
            var secureOption = enableSsl
                ? (port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
                : SecureSocketOptions.None;

            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync(host, port, secureOption);
            await client.AuthenticateAsync(userName, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        private static string EnsureStandardSupportFooter(string htmlBody)
        {
            var body = htmlBody ?? string.Empty;
            if (body.IndexOf("data-els-support-footer", StringComparison.OrdinalIgnoreCase) >= 0
                || (body.IndexOf("mailto:techforgeacademydev@gmail.com", StringComparison.OrdinalIgnoreCase) >= 0
                    && body.IndexOf("https://wa.me/919059261259", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return body;
            }

            var hasHelpLine = body.IndexOf("If you need help, please contact LMS support.", StringComparison.OrdinalIgnoreCase) >= 0;
            var sb = new StringBuilder(body.TrimEnd());
            sb.Append("<div data-els-support-footer='true' style='margin-top:16px;padding-top:12px;border-top:1px solid #e5e7eb;font-family:Segoe UI,Arial,sans-serif;color:#1f2937;font-size:14px;line-height:1.6;'>");
            sb.Append("<p style='margin:0 0 8px;font-weight:600;'>Contact Support</p>");
            sb.Append("<p style='margin:0 0 4px;'>Email: <a href='mailto:techforgeacademydev@gmail.com' style='color:#1d4ed8;text-decoration:none;'>techforgeacademydev@gmail.com</a></p>");
            sb.Append("<p style='margin:0 0 4px;'>WhatsApp: <a href='https://wa.me/919059261259' style='color:#1d4ed8;text-decoration:none;'>+91 90592 61259</a></p>");
            sb.Append("<p style='margin:0;'>Website: <a href='https://els.techforgedev.in/' style='color:#1d4ed8;text-decoration:none;'>https://els.techforgedev.in/</a></p>");
            if (!hasHelpLine)
            {
                sb.Append("<p style='margin:10px 0 0;'>If you need help, please contact LMS support.</p>");
            }

            sb.Append("</div>");

            return sb.ToString();
        }

        private sealed class EffectiveMailSettings
        {
            public string Host { get; set; }
            public int Port { get; set; }
            public bool? EnableSsl { get; set; }
            public string FromAddress { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
        }

        private async Task<EffectiveMailSettings> ResolveTenantMailConfigurationAsync(string tenantId, string tenantEmail)
        {
            var defaults = new EffectiveMailSettings
            {
                Host = _mailConfiguration.SmtpServer,
                Port = _mailConfiguration.Port,
                EnableSsl = _mailConfiguration.EnableSsl,
                FromAddress = _mailConfiguration.MailFrom,
                Username = _mailConfiguration.Username,
                Password = _mailConfiguration.Password
            };

            var candidateTenantNames = new List<string>();

            void addTenantNameCandidate(string value)
            {
                var normalized = value?.Trim();
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    return;
                }

                if (!candidateTenantNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    candidateTenantNames.Add(normalized);
                }
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(tenantId))
                {
                    var tenant = await _unitOfWork.DbContext.Tenants
                        .Where(t => t.TenantId == tenantId)
                        .Select(t => new { t.TenantName, t.Email })
                        .FirstOrDefaultAsync();

                    addTenantNameCandidate(tenant?.TenantName);
                    addTenantNameCandidate(tenant?.Email);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Unable to resolve tenant record by TenantId {TenantId} while loading tenant SMTP integration.", tenantId);
            }

            addTenantNameCandidate(tenantEmail);

            if (candidateTenantNames.Count == 0)
            {
                return defaults;
            }

            string encryptedPayload = null;
            try
            {
                var connection = _unitOfWork.DbContext.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                foreach (var candidateTenantName in candidateTenantNames)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = @"
SELECT TOP (1) [EncryptedSettingsJson]
FROM [dbo].[TenantIntegrationSettings]
WHERE [TenantName] = @tenantName
  AND [Category] = 'Email'
  AND (ISNULL([ProviderKey], '') = '' OR [ProviderKey] = 'smtp')
  AND [IsEnabled] = 1
ORDER BY CASE WHEN ISNULL([StoreCode], '') = '' THEN 0 ELSE 1 END, [ModifiedDate] DESC";

                    var tenantNameParam = command.CreateParameter();
                    tenantNameParam.ParameterName = "@tenantName";
                    tenantNameParam.Value = candidateTenantName;
                    command.Parameters.Add(tenantNameParam);

                    var obj = await command.ExecuteScalarAsync();
                    encryptedPayload = obj?.ToString();
                    if (!string.IsNullOrWhiteSpace(encryptedPayload))
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is SqlException sqlEx && sqlEx.Number == 208)
            {
                _logger?.LogDebug("TenantIntegrationSettings table not found while resolving tenant email integration. Falling back to global MailConfiguration.");
                return defaults;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Unable to resolve tenant email integration settings. Falling back to global MailConfiguration. TenantId: {TenantId}", tenantId);
                return defaults;
            }

            if (string.IsNullOrWhiteSpace(encryptedPayload))
            {
                return defaults;
            }

            try
            {
                var decrypted = DecryptIntegrationPayload(encryptedPayload);
                if (string.IsNullOrWhiteSpace(decrypted))
                {
                    return defaults;
                }

                using var json = JsonDocument.Parse(decrypted);
                var root = json.RootElement;
                var publicObj = root.TryGetProperty("Public", out var pub) ? pub : default;
                var secretObj = root.TryGetProperty("Secret", out var sec) ? sec : default;

                static string getValue(JsonElement element, string key)
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        return null;
                    }

                    foreach (var property in element.EnumerateObject())
                    {
                        if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                        {
                            return property.Value.GetString()?.Trim();
                        }
                    }

                    return null;
                }

                var host = getValue(publicObj, "Host");
                var portText = getValue(publicObj, "Port");
                var from = getValue(publicObj, "From");
                var sslText = getValue(publicObj, "EnableSsl");
                var user = getValue(secretObj, "Username");
                var pass = getValue(secretObj, "Password");

                if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
                {
                    return defaults;
                }

                return new EffectiveMailSettings
                {
                    Host = string.IsNullOrWhiteSpace(host) ? defaults.Host : host,
                    Port = int.TryParse(portText, out var p) ? p : (defaults.Port > 0 ? defaults.Port : 587),
                    FromAddress = string.IsNullOrWhiteSpace(from) ? defaults.FromAddress : from,
                    Username = user,
                    Password = pass,
                    EnableSsl = string.IsNullOrWhiteSpace(sslText)
                        ? defaults.EnableSsl
                        : !string.Equals(sslText, "false", StringComparison.OrdinalIgnoreCase)
                };
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Tenant email integration payload parsing failed. Falling back to global MailConfiguration. TenantId: {TenantId}", tenantId);
                return defaults;
            }
        }

        private string DecryptIntegrationPayload(string encryptedText)
        {
            if (string.IsNullOrWhiteSpace(encryptedText) || !encryptedText.Contains(':'))
            {
                return null;
            }

            var parts = encryptedText.Split(':');
            if (parts.Length != 2)
            {
                return null;
            }

            var iv = Convert.FromBase64String(parts[0]);
            var cipherBytes = Convert.FromBase64String(parts[1]);

            using var aes = Aes.Create();
            aes.Key = BuildTenantIntegrationEncryptionKey();
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }

        private byte[] BuildTenantIntegrationEncryptionKey()
        {
            var keyMaterial = _configuration?["IntegrationSettings:EncryptionKey"];
            if (string.IsNullOrWhiteSpace(keyMaterial))
            {
                keyMaterial = _configuration?.GetConnectionString("MasterDb")
                    ?? _configuration?.GetConnectionString("IdentityDb")
                    ?? string.Empty;
            }

            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(keyMaterial));
        }
    }
}
