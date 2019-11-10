using System;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;
using System.Reflection;
using System.IO;

namespace LMS.SwaggerUI
{
    public class SwaggerConfig
    {
        public static void SwaggerGen(SwaggerGenOptions genOptions, SwaggerAPIMetaData metaData)
        {
            genOptions.SwaggerDoc(metaData.Name, metaData.SwaggerInfo);
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.XML";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

            //Swagger to use those XML comments.
            // genOptions.IncludeXmlComments(xmlPath);

            genOptions.IgnoreObsoleteActions();


            //TODO: Need to Add Authentication Module


        }
    }
}
