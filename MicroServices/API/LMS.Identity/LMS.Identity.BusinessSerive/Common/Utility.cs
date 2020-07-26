using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LMS.Identity.BusinessSerive.Common
{
    public static class Utility
    {
        #region Encode Password
        public static string encode(string email,int length)
        {
         return  GenerateDynamicCryptoString(email, length);
        }
        #endregion

        public static string GenerateDynamicCryptoString(string pattern, int length, bool isPassword = false)
        {
            if (length != 0)
            {
                RNGCryptoServiceProvider provider = CreateRNGCryptoServiceProvider();
                StringBuilder sb;
                do
                {
                    sb = new StringBuilder();
                    var byteArray = new byte[length > 3 ? length : 4];
                    provider.GetBytes(byteArray);

                    int patternLength = pattern.Length;

                    //Gets character on index based from the pattern passed
                    for (var i = 0; i < byteArray.Length; i++)
                    {
                        byte x = byteArray[i];
                        while (x >= patternLength)
                            x = Convert.ToByte(x % patternLength);
                        sb.Append(pattern[x]);
                    }

                    //Below condition checks if atleast one special character is avaible in generated string.
                    isPassword = HasSpecialCharacters(sb, pattern, isPassword);
                } while (isPassword);

                return sb.ToString();
            }

            return string.Empty;
        }
         private static bool HasSpecialCharacters(StringBuilder sb, string pattern, bool isPassword)
        {
            //Below condition checks if atleast one special character is avaible in generated string.
            if (isPassword)
            {
                var regex = new Regex("[a-zA-Z0-9]*");
                var specialChars = regex.Replace(pattern, "");
                if (!string.IsNullOrEmpty(specialChars))
                {
                    var specialCharsRegex = new Regex("[" + specialChars + "]");
                    isPassword = !specialCharsRegex.IsMatch(sb.ToString());
                }
                else
                    isPassword = false;
            }
            return isPassword;
        }


        private static RNGCryptoServiceProvider CreateRNGCryptoServiceProvider()
        {
            RNGCryptoServiceProvider rngCryptoServiceProvider = new RNGCryptoServiceProvider();
            return rngCryptoServiceProvider;
        }

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
