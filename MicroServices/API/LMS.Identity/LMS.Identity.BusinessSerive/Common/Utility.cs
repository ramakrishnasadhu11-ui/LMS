using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LMS.Identity.BusinessSerive.Common
{
    public static class Utility
    {
        #region Encode Password
        // Characters that are easy to confuse in an email client (0/O, 1/l/I) are excluded so a
        // mailed password can be transcribed reliably.
        private const string PasswordUpperChars = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string PasswordLowerChars = "abcdefghijkmnopqrstuvwxyz";
        private const string PasswordDigitChars = "23456789";
        private const string PasswordSpecialChars = "!#$%&*+-=?@";

        /// <summary>
        /// Generates a temporary password from a fixed, unambiguous character set.
        /// The supplied email is no longer used as the character pool: doing so produced
        /// passwords such as "i1iiia@d" that users could not reliably retype.
        /// </summary>
        public static string encode(string email, int length)
        {
            return GeneratePassword(length);
        }

        public static string GeneratePassword(int length)
        {
            if (length <= 0)
            {
                return string.Empty;
            }

            // Guarantee one character from each class, then fill the remainder at random.
            var classes = new[] { PasswordUpperChars, PasswordLowerChars, PasswordDigitChars, PasswordSpecialChars };
            var allChars = string.Concat(classes);
            var chars = new char[length];

            for (var i = 0; i < length; i++)
            {
                var pool = i < classes.Length ? classes[i] : allChars;
                chars[i] = pool[RandomNumberGenerator.GetInt32(0, pool.Length)];
            }

            // Shuffle so the guaranteed characters are not always in the same positions.
            for (var i = chars.Length - 1; i > 0; i--)
            {
                var j = RandomNumberGenerator.GetInt32(0, i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            return new string(chars);
        }
        #endregion

        public static string GenerateDynamicCryptoString(string pattern, int length, bool isPassword = false)
        {
            if (length != 0)
            {
                StringBuilder sb;
                do
                {
                    sb = new StringBuilder();
                    var byteArray = new byte[length > 3 ? length : 4];
                    RandomNumberGenerator.Fill(byteArray);

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
            for (int i = 0; i < iOTPLength; i++)
            {
                sTempChars = saAllowedCharacters[RandomNumberGenerator.GetInt32(0, saAllowedCharacters.Length)];
                sOTP += sTempChars;
            }
            return sOTP;

        }

        public static string GenerateStoreCode(int Length)
        {
            const string _allowedChars = "ABCDEFGHJKLMNOPQRSTUVWXYZ";
            char[] chars = new char[Length];

            for (int i = 0; i < Length; i++)
            {
                chars[i] = _allowedChars[RandomNumberGenerator.GetInt32(0, _allowedChars.Length)];
            }
            return new string(chars);
        }


    }
}
