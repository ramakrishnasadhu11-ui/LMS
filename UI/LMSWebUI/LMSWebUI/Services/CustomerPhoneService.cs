using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PhoneNumbers;
using System.Text.RegularExpressions;

namespace LMSWebUI.Services
{
    public sealed class CustomerPhoneCountry
    {
        public string Iso2 { get; init; }
        public string Name { get; init; }
        public string CallingCode { get; init; }
        public string Flag { get; init; }
    }

    public sealed class CustomerPhoneValidationResult
    {
        public bool IsValid { get; init; }
        public string NormalizedNumber { get; init; }
        public string FormattedNationalNumber { get; init; }
        public string Message { get; init; }
        public bool UsedLegacyCompatibility { get; init; }
    }

    public interface ICustomerPhoneService
    {
        IReadOnlyList<CustomerPhoneCountry> GetSupportedCountries();
        CustomerPhoneCountry FindCountry(string iso2);
        CustomerPhoneCountry FindCountryByName(string name);
        CustomerPhoneValidationResult ValidateAndNormalize(string phone, string countryIso2, bool allowLegacyCompatibility);
        string GetLocalNumber(string phone, string countryIso2);
        string GetRegionForNumber(string phone);
    }

    public sealed class CustomerPhoneService : ICustomerPhoneService
    {
        private readonly PhoneNumberUtil _phoneUtil = PhoneNumberUtil.GetInstance();
        private static readonly Regex IndianMobileRegex = new("^[6-9][0-9]{9}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public IReadOnlyList<CustomerPhoneCountry> GetSupportedCountries()
        {
            return _phoneUtil.GetSupportedRegions()
                .Where(region => region.Length == 2 && !string.Equals(region, "001", StringComparison.OrdinalIgnoreCase))
                .Select(CreateCountry)
                .Where(country => country != null)
                .OrderBy(country => country.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public CustomerPhoneCountry FindCountry(string iso2)
        {
            if (string.IsNullOrWhiteSpace(iso2))
            {
                return null;
            }

            var region = iso2.Trim().ToUpperInvariant();
            return _phoneUtil.GetSupportedRegions().Contains(region, StringComparer.OrdinalIgnoreCase)
                ? CreateCountry(region)
                : null;
        }

        public CustomerPhoneCountry FindCountryByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return GetSupportedCountries().FirstOrDefault(country =>
                string.Equals(country.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase));
        }

        public CustomerPhoneValidationResult ValidateAndNormalize(string phone, string countryIso2, bool allowLegacyCompatibility)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return new CustomerPhoneValidationResult { IsValid = true };
            }

            var input = phone.Trim();
            var region = FindCountry(countryIso2)?.Iso2;

            if (!string.IsNullOrWhiteSpace(countryIso2) && region == null)
            {
                return Invalid("Select a supported country to validate this phone number.");
            }

            if (allowLegacyCompatibility && region == null && IsLegacyDigits(input))
            {
                return new CustomerPhoneValidationResult
                {
                    IsValid = true,
                    NormalizedNumber = new string(input.Where(char.IsDigit).ToArray()),
                    UsedLegacyCompatibility = true
                };
            }

            if (region == null && !input.StartsWith("+", StringComparison.Ordinal))
            {
                return Invalid("Select a supported country to validate this phone number.");
            }

            var indianLocalNumber = input.StartsWith("+91", StringComparison.Ordinal)
                ? input[3..]
                : input;

            if (string.Equals(region, "IN", StringComparison.OrdinalIgnoreCase)
                && !IndianMobileRegex.IsMatch(indianLocalNumber))
            {
                return Invalid(BuildInvalidMessage(region));
            }

            if (string.Equals(region, "IN", StringComparison.OrdinalIgnoreCase))
            {
                return new CustomerPhoneValidationResult
                {
                    IsValid = true,
                    NormalizedNumber = "+91" + indianLocalNumber,
                    FormattedNationalNumber = "0" + indianLocalNumber
                };
            }

            try
            {
                var parsed = _phoneUtil.ParseAndKeepRawInput(input, region);
                if (!_phoneUtil.IsValidNumber(parsed))
                {
                    return Invalid(BuildInvalidMessage(region));
                }

                if (!allowLegacyCompatibility
                    && input.StartsWith("+", StringComparison.Ordinal)
                    && !string.Equals(_phoneUtil.GetRegionCodeForNumber(parsed), region, StringComparison.OrdinalIgnoreCase))
                {
                    return Invalid(BuildInvalidMessage(region));
                }

                if (string.Equals(region, "IN", StringComparison.OrdinalIgnoreCase)
                    && (_phoneUtil.GetNumberType(parsed) != PhoneNumberType.MOBILE
                        || _phoneUtil.GetNationalSignificantNumber(parsed).Length != 10))
                {
                    return Invalid(BuildInvalidMessage(region));
                }

                return new CustomerPhoneValidationResult
                {
                    IsValid = true,
                    NormalizedNumber = _phoneUtil.Format(parsed, PhoneNumberFormat.E164),
                    FormattedNationalNumber = _phoneUtil.Format(parsed, PhoneNumberFormat.NATIONAL)
                };
            }
            catch (NumberParseException)
            {
                return Invalid(BuildInvalidMessage(region));
            }
        }

        public string GetLocalNumber(string phone, string countryIso2)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return null;
            }

            var input = phone.Trim();
            var region = FindCountry(countryIso2)?.Iso2;

            if (string.Equals(region, "IN", StringComparison.OrdinalIgnoreCase))
            {
                if (input.StartsWith("+91", StringComparison.Ordinal))
                {
                    var nationalNumber = input[3..];
                    return IndianMobileRegex.IsMatch(nationalNumber)
                        ? nationalNumber
                        : input;
                }

                return input;
            }

            if (!input.StartsWith("+", StringComparison.Ordinal))
            {
                return input;
            }

            try
            {
                var parsed = _phoneUtil.ParseAndKeepRawInput(input, null);
                if (string.Equals(_phoneUtil.GetRegionCodeForNumber(parsed), region, StringComparison.OrdinalIgnoreCase))
                {
                    return _phoneUtil.GetNationalSignificantNumber(parsed);
                }
            }
            catch (NumberParseException)
            {
                // Preserve the stored value when it cannot be parsed for display.
            }

            return input;
        }

        public string GetRegionForNumber(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone) || !phone.TrimStart().StartsWith("+", StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                var parsed = _phoneUtil.Parse(phone.Trim(), null);
                return _phoneUtil.GetRegionCodeForNumber(parsed);
            }
            catch (NumberParseException)
            {
                return null;
            }
        }

        private CustomerPhoneCountry CreateCountry(string region)
        {
            try
            {
                var name = new RegionInfo(region).EnglishName;
                return new CustomerPhoneCountry
                {
                    Iso2 = region,
                    Name = name,
                    CallingCode = "+" + _phoneUtil.GetCountryCodeForRegion(region),
                    Flag = ToFlag(region)
                };
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static bool IsLegacyDigits(string input)
        {
            var digits = new string(input.Where(char.IsDigit).ToArray());
            return digits.Length is >= 7 and <= 15 && digits.Length == input.Length;
        }

        private static CustomerPhoneValidationResult Invalid(string message) => new()
        {
            IsValid = false,
            Message = message
        };

        private string BuildInvalidMessage(string region)
        {
            if (string.Equals(region, "IN", StringComparison.OrdinalIgnoreCase))
            {
                return "Enter a valid 10-digit Indian mobile number.";
            }

            var country = FindCountry(region);
            return country == null
                ? "Enter a valid phone number for the selected country."
                : $"Enter a valid {country.Name} phone number.";
        }

        private static string ToFlag(string region)
        {
            return string.Concat(region.ToUpperInvariant().Select(character => char.ConvertFromUtf32(character + 0x1F1A5)));
        }
    }
}
