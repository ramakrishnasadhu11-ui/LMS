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
        var result = _service.ValidateAndNormalize("98765 43210", "IN", false);

        Assert.True(result.IsValid);
        Assert.Equal("+919876543210", result.NormalizedNumber);
        Assert.Equal("098765 43210", result.FormattedNationalNumber);
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
    public void EmptyPhone_HasNoE164ValueForSaveValidation()
    {
        var result = _service.ValidateAndNormalize(string.Empty, "IN", false);

        Assert.False(result.IsValid);
        Assert.Null(result.NormalizedNumber);
        Assert.Null(result.FormattedNationalNumber);
    }
}
