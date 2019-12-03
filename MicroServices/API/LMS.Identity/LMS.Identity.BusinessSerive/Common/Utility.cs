using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Identity.BusinessSerive.Common
{
    public static class Utility
    {
        #region Encode Password
        public static string encode(string email)
        {
            char strChar;
            int bcode = 212;
            string endecode = string.Empty;

            while (email.Length > 0)
            {
                strChar = email[0];
                email = email.Substring(1, email.Length - 1);
                strChar = Strings.ChrW(bcode ^ Strings.AscW(strChar));

                endecode += strChar;
            }
            return endecode;
        }
        #endregion


    }
}
