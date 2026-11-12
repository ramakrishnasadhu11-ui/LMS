using LMSWebUI.Services;
using Xunit;

namespace LMSWebUI.Tests;

public sealed class CustomerPhoneServiceTests
{
    private readonly CustomerPhoneService _service = new();

    [Fact]
    public void CountrySpecificNumber_IsNormalizedToE164()
    {
        var result = _service.ValidateAndNormalize("(415) 555-2671", "US", false);

        Assert.True(result.IsValid);
        Assert.Equal("+14155552671", result.NormalizedNumber);
    }

    [Fact]
    public void CountrySpecificNationalNumber_UsesSelectedCountry()
    {
        var result = _service.ValidateAndNormalize("020 7946 0018", "GB", false);

        Assert.True(result.IsValid);
        Assert.Equal("+442079460018", result.NormalizedNumber);
    }

    [Fact]
    public void InvalidNumber_ReturnsFriendlyValidationMessage()
    {
        var result = _service.ValidateAndNormalize("123", "US", false);

        Assert.False(result.IsValid);
        Assert.Contains("United States", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingCountryConfiguration_PreservesLegacyDigits()
    {
        var result = _service.ValidateAndNormalize("9876543210", null, true);

        Assert.True(result.IsValid);
        Assert.Equal("9876543210", result.NormalizedNumber);
        Assert.True(result.UsedLegacyCompatibility);
    }

    [Fact]
    public void UnsupportedCountry_RequiresSupportedCountrySelection()
    {
        var result = _service.ValidateAndNormalize("9876543210", "ZZ", false);

        Assert.False(result.IsValid);
        Assert.Contains("supported country", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportedCountryMetadata_IncludesCallingCodeAndFlag()
    {
        var country = _service.FindCountry("US");

        Assert.NotNull(country);
        Assert.Equal("+1", country.CallingCode);
        Assert.Equal("🇺🇸", country.Flag);
    }

    [Fact]
    public void ExistingInternationalNumber_RemainsCompatible()
    {
        var result = _service.ValidateAndNormalize("+44 20 7946 0018", "US", true);

        Assert.True(result.IsValid);
        Assert.Equal("+442079460018", result.NormalizedNumber);
    }

    [Fact]
    public void StrictValidation_DoesNotAcceptLegacyDigitsWithoutCountry()
    {
        var result = _service.ValidateAndNormalize("9876543210", null, false);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void IndianMobile_IsNormalizedAndNationallyFormatted()
    {
        var result = _service.ValidateAndNormalize("9876543210", "IN", false);

        Assert.True(result.IsValid);
        Assert.Equal("+919876543210", result.NormalizedNumber);
        Assert.Equal("09876543210", result.FormattedNationalNumber);
    }

    [Fact]
    public void IndianE164Number_IsSeparatedForLocalInput()
    {
        var result = _service.GetLocalNumber("+917981595801", "IN");

        Assert.Equal("7981595801", result);
    }

    [Fact]
    public void IndianLocalNumber_IsNotPrefixedForLocalInput()
    {
        var result = _service.GetLocalNumber("7981595801", "IN");

        Assert.Equal("7981595801", result);
    }

    [Fact]
    public void InvalidIndianLength_UsesIndianSpecificMessage()
    {
        var result = _service.ValidateAndNormalize("987654321", "IN", false);

        Assert.False(result.IsValid);
        Assert.Equal("Enter a valid 10-digit Indian mobile number.", result.Message);
    }

    [Fact]
    public void CountrySwitch_UsesNewCountryRules()
    {
        var result = _service.ValidateAndNormalize("020 7946 0018", "US", false);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidUsNumber_ReturnsE164()
    {
        var result = _service.ValidateAndNormalize("202-555-0125", "US", false);

        Assert.True(result.IsValid);
        Assert.Equal("+12025550125", result.NormalizedNumber);
    }

    [Fact]
    public void InvalidUkNumber_IsRejected()
    {
        var result = _service.ValidateAndNormalize("020 7946", "GB", false);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void EmptyPhone_IsValidForOptionalPhone()
    {
        var result = _service.ValidateAndNormalize(string.Empty, "IN", false);

        Assert.True(result.IsValid);
        Assert.Null(result.NormalizedNumber);
        Assert.Null(result.FormattedNationalNumber);
    }

    [Theory]
    [InlineData("7981595801")]
    [InlineData("6981595801")]
    [InlineData("8981595801")]
    [InlineData("9981595801")]
    public void IndianMobile_StartingWithSixThroughNine_IsValid(string phone)
    {
        var result = _service.ValidateAndNormalize(phone, "IN", false);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("798159580")]
    [InlineData("79815958012")]
    [InlineData("79815 95801")]
    [InlineData("79815-95801")]
    [InlineData("79815abc01")]
    public void IndianMobile_InvalidFormat_IsRejected(string phone)
    {
        var result = _service.ValidateAndNormalize(phone, "IN", false);

        Assert.False(result.IsValid);
        Assert.Equal("Enter a valid 10-digit Indian mobile number.", result.Message);
    }

    [Theory]
    [InlineData("2025550125", "US", "+12025550125")]
    [InlineData("02079460018", "GB", "+442079460018")]
    [InlineData("4165550123", "CA", "+14165550123")]
    [InlineData("0293744000", "AU", "+61293744000")]
    [InlineData("0501234567", "AE", "+971501234567")]
    public void InternationalLocalNumber_IsValidatedAndNormalized(string phone, string country, string expectedE164)
    {
        var result = _service.ValidateAndNormalize(phone, country, false);

        Assert.True(result.IsValid);
        Assert.Equal(expectedE164, result.NormalizedNumber);
    }

    [Theory]
    [InlineData("123", "US")]
    [InlineData("0207946", "GB")]
    [InlineData("416555", "CA")]
    [InlineData("02937", "AU")]
    [InlineData("041234", "AE")]
    public void InternationalInvalidLocalNumber_IsRejected(string phone, string country)
    {
        var result = _service.ValidateAndNormalize(phone, country, false);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("+12025550125", "US", "2025550125")]
    [InlineData("+442079460018", "GB", "2079460018")]
    [InlineData("+14165550123", "CA", "4165550123")]
    [InlineData("+61293744000", "AU", "293744000")]
    [InlineData("+971501234567", "AE", "501234567")]
    public void InternationalE164Number_IsSeparatedForLocalInput(string storedPhone, string country, string expectedLocal)
    {
        var result = _service.GetLocalNumber(storedPhone, country);

        Assert.Equal(expectedLocal, result);
    }
}
