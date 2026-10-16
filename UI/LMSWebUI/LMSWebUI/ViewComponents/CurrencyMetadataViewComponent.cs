using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using LMSWebUI.Services;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.ViewComponents;

namespace LMSWebUI.ViewComponents
{
    public class CurrencyMetadataViewComponent : ViewComponent
    {
        private readonly ICurrencyService _currencyService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<CurrencyMetadataViewComponent> _logger;

        public CurrencyMetadataViewComponent(ICurrencyService currencyService, IHttpContextAccessor httpContextAccessor, ILogger<CurrencyMetadataViewComponent> logger)
        {
            _currencyService = currencyService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            try
            {
                var httpContext = _httpContextAccessor?.HttpContext ?? this.ViewContext?.HttpContext;
                var tenant = httpContext?.Session?.GetString("TenantName") ?? string.Empty;
                var store = httpContext?.Session?.GetString("TenantStore") ?? string.Empty;

                if (_currencyService == null)
                {
                    return Content("<script>window.__currency = {};</script>");
                }

                var cfg = await _currencyService.GetEffectiveCurrencyAsync(tenant ?? string.Empty, store ?? string.Empty);
                var payload = new { symbol = cfg?.CurrencySymbol, locale = cfg?.CurrencyLocale, digits = cfg?.DecimalDigits, code = cfg?.CurrencyCode };
                var json = JsonConvert.SerializeObject(payload);
                var script = $"<script>window.__currency = {json};</script>";
                // Return as HtmlContent so Razor does not HTML-encode the script tag and
                // the browser executes it instead of rendering it as text.
                return new HtmlContentViewComponentResult(new HtmlString(script));
            }
            catch (Exception ex)
            {
                try { _logger?.LogWarning(ex, "Currency metadata rendering failed"); } catch { }
                return new HtmlContentViewComponentResult(new HtmlString("<script>window.__currency = {};</script>"));
            }
        }
    }
}
