using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace LMS.Identity.WebApi.Api.Utility
{
    public class Common
    {
        public static string GetEnumDescription(Enum enumVal)
        {
            System.Reflection.MemberInfo[] memInfo = enumVal.GetType().GetMember(enumVal.ToString());
            DescriptionAttribute attribute = CustomAttributeExtensions.GetCustomAttribute<DescriptionAttribute>(memInfo[0]);
            return attribute.Description;
        }
    }
}
