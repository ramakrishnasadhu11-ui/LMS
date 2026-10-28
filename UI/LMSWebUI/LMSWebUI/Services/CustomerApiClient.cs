using LMSClientFactory.Helper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LMSWebUI.Services
{
    public class CustomerApiClient : HttpClientApi, ICustomerApiClient
    {
        private const string AccessTokenSessionKey = "AccessToken";
        private const string AccessTokenExpiresAtUtcSessionKey = "AccessTokenExpiresAtUtc";
        private const string ReLoginMessage = "Session expired. Please log in again.";

        public CustomerApiClient(IOptions<ServiceEndpointsOptions> options, IHttpContextAccessor httpContextAccessor)
            : base(
                  options.Value.Master.BaseUrl.TrimEnd('/') + "/Customer",
                  ResolveAccessToken(httpContextAccessor),
                  ResolveAuthenticationValidationError(httpContextAccessor))
        {
        }

        private static string ResolveAccessToken(IHttpContextAccessor httpContextAccessor)
        {
            if (!TryGetValidAccessToken(httpContextAccessor, out var accessToken, out _))
            {
                return string.Empty;
            }

            return accessToken;
        }

        private static string ResolveAuthenticationValidationError(IHttpContextAccessor httpContextAccessor)
        {
            return TryGetValidAccessToken(httpContextAccessor, out _, out var validationError)
                ? string.Empty
                : validationError;
        }

        private static bool TryGetValidAccessToken(IHttpContextAccessor httpContextAccessor, out string accessToken, out string validationError)
        {
            accessToken = string.Empty;
            validationError = ReLoginMessage;

            var httpContext = httpContextAccessor?.HttpContext;
            if (httpContext == null)
            {
                return false;
            }

            var sessionToken = httpContext.Session?.GetString(AccessTokenSessionKey);
            if (!string.IsNullOrWhiteSpace(sessionToken))
            {
                accessToken = sessionToken.Trim();
            }
            else
            {
                return false;
            }

            var expiresAtRaw = httpContext.Session?.GetString(AccessTokenExpiresAtUtcSessionKey);
            var expiresAtUtc = DateTime.MinValue;
            var hasValidExpiry = !string.IsNullOrWhiteSpace(expiresAtRaw)
                && DateTime.TryParse(
                    expiresAtRaw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out expiresAtUtc);

            if (!hasValidExpiry)
            {
                hasValidExpiry = TryGetTokenExpiryFromJwt(accessToken, out expiresAtUtc);
                if (hasValidExpiry)
                {
                    httpContext.Session?.SetString(AccessTokenExpiresAtUtcSessionKey, expiresAtUtc.ToUniversalTime().ToString("O"));
                }
            }

            if (!hasValidExpiry || expiresAtUtc.ToUniversalTime() <= DateTime.UtcNow)
            {
                httpContext.Session?.Remove(AccessTokenSessionKey);
                httpContext.Session?.Remove(AccessTokenExpiresAtUtcSessionKey);
                accessToken = string.Empty;
                return false;
            }

            validationError = string.Empty;
            return true;
        }

        private static bool TryGetTokenExpiryFromJwt(string accessToken, out DateTime expiresAtUtc)
        {
            expiresAtUtc = default;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return false;
            }

            var tokenParts = accessToken.Split('.');
            if (tokenParts.Length < 2)
            {
                return false;
            }

            if (!TryDecodeJwtPayload(tokenParts[1], out var payloadJson))
            {
                return false;
            }

            try
            {
                using var payload = JsonDocument.Parse(payloadJson);
                if (!payload.RootElement.TryGetProperty("exp", out var expElement))
                {
                    return false;
                }

                long expUnixSeconds;
                if (expElement.ValueKind == JsonValueKind.Number)
                {
                    if (!expElement.TryGetInt64(out expUnixSeconds))
                    {
                        return false;
                    }
                }
                else if (expElement.ValueKind == JsonValueKind.String)
                {
                    if (!long.TryParse(expElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out expUnixSeconds))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expUnixSeconds).UtcDateTime;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDecodeJwtPayload(string jwtPayload, out string payloadJson)
        {
            payloadJson = string.Empty;
            if (string.IsNullOrWhiteSpace(jwtPayload))
            {
                return false;
            }

            try
            {
                var padded = jwtPayload.Replace('-', '+').Replace('_', '/');
                var remainder = padded.Length % 4;
                if (remainder > 0)
                {
                    padded = padded.PadRight(padded.Length + (4 - remainder), '=');
                }

                var bytes = Convert.FromBase64String(padded);
                payloadJson = Encoding.UTF8.GetString(bytes);
                return !string.IsNullOrWhiteSpace(payloadJson);
            }
            catch
            {
                return false;
            }
        }
    }
}
