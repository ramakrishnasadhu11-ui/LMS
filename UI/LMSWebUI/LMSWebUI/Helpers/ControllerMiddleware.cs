using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace LMSWebUI.Helpers
{
    public class ControllerMiddleware
    {
         private readonly RequestDelegate _next;

          public ControllerMiddleware(RequestDelegate next)
        {
            _next = next;
        }
         public async Task Invoke(HttpContext context)
        {
            var endpointFeature = context.Features[typeof(IEndpointFeature)] as IEndpointFeature;
            var endpoint = endpointFeature?.Endpoint;

            //note: endpoint will be null, if there was no resolved route
            if (endpoint == null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }
            await _next.Invoke(context);
        }
    }
}
