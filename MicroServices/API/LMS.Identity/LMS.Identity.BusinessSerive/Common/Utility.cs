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

        public static string generateOTP()
        {
            string[] saAllowedCharacters = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
            string sRandomOTP = GenerateRandomOTP(8, saAllowedCharacters);
            return sRandomOTP;
        }

        private static string GenerateRandomOTP(int iOTPLength, string[] saAllowedCharacters)
        {
            string sOTP = String.Empty;
            string sTempChars = String.Empty;
            Random rand = new Random();
            for (int i = 0; i < iOTPLength; i++)
            {
                int p = rand.Next(0, saAllowedCharacters.Length);
                sTempChars = saAllowedCharacters[rand.Next(0, saAllowedCharacters.Length)];
                sOTP += sTempChars;
            }
            return sOTP;

        }

        public static string GenerateStoreCode(int Length)
        {
            string _allowedChars = "ABCDEFGHJKLMNOPQRSTUVWXYZ";
            Random randNum = new Random();
            char[] chars = new char[Length];

            for (int i = 0; i < Length; i++)
            {
                chars[i] = _allowedChars[Convert.ToInt32((_allowedChars.Length - 1) * randNum.NextDouble())];
            }
            return new string(chars);
        }

    }
}
