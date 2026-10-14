using System;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.DependencyInjection;
using LMSWebUI.Services;

namespace LMSWebUI.Helpers
{
    public static class CurrencyHtmlHelpers
    {
        /// <summary>
        /// Formats a monetary amount according to the resolved tenant/store currency configuration.
        /// If tenant/store cannot be resolved, falls back to the legacy currency.
        /// </summary>
        public static IHtmlContent FormatCurrency(this IHtmlHelper html, decimal? amount)
        {
            if (amount == null)
            {
                return HtmlString.Empty;
            }
            try
            {
                var svc = html.ViewContext.HttpContext.RequestServices.GetService(typeof(ICurrencyService)) as ICurrencyService;
                if (svc == null)
                {
                    return new HtmlString(((decimal)amount).ToString());
                }

                // Resolve tenant/store from session (server-side authoritative source)
                var tenant = html.ViewContext.HttpContext.Session.GetString("TenantName");
                var store = html.ViewContext.HttpContext.Session.GetString("TenantStore");
                var cfg = svc.GetEffectiveCurrencyAsync(tenant ?? string.Empty, store ?? string.Empty).GetAwaiter().GetResult();
                var formatted = svc.Format(amount.Value, cfg);

                return new HtmlString(formatted);
            }
            catch
            {
                return new HtmlString(((decimal)amount).ToString());
            }
        }

        /// <summary>
        /// Async variant suitable for awaiting from Razor views: @await Html.FormatCurrencyAsync(amount)
        /// Preserves behavior of the synchronous helper but avoids blocking on async calls.
        /// </summary>
        public static async System.Threading.Tasks.Task<IHtmlContent> FormatCurrencyAsync(this IHtmlHelper html, decimal? amount)
        {
            if (amount == null)
            {
                return HtmlString.Empty;
            }

            try
            {
                var svc = html.ViewContext.HttpContext.RequestServices.GetService(typeof(ICurrencyService)) as ICurrencyService;
                if (svc == null)
                {
                    return new HtmlString(((decimal)amount).ToString());
                }

                var tenant = html.ViewContext.HttpContext.Session.GetString("TenantName");
                var store = html.ViewContext.HttpContext.Session.GetString("TenantStore");
                var cfg = await svc.GetEffectiveCurrencyAsync(tenant ?? string.Empty, store ?? string.Empty);
                var formatted = svc.Format(amount.Value, cfg);

                return new HtmlString(formatted);
            }
            catch
            {
                return new HtmlString(((decimal)amount).ToString());
            }
        }

        // Generic overloads to support IHtmlHelper<TModel> in Razor views where the
        // generic interface is used. These forward to the non-generic implementations
        // to avoid duplicate logic and preserve existing behavior.
        public static IHtmlContent FormatCurrency<TModel>(this IHtmlHelper<TModel> html, decimal? amount)
        {
            return FormatCurrency((IHtmlHelper)html, amount);
        }

        public static System.Threading.Tasks.Task<IHtmlContent> FormatCurrencyAsync<TModel>(this IHtmlHelper<TModel> html, decimal? amount)
        {
            return FormatCurrencyAsync((IHtmlHelper)html, amount);
        }
    }
}
