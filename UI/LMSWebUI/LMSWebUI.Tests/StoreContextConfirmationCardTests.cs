using System;
using System.IO;
using Xunit;

namespace LMSWebUI.Tests
{
    public class StoreContextConfirmationCardTests
    {
        private static string LoginViewContents()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LMSWebUI", "Views", "Login", "Login.cshtml");
            return File.ReadAllText(Path.GetFullPath(path));
        }

        [Fact]
        public void LoginView_ContainsStoreContextConfirmationCardContent()
        {
            var view = LoginViewContents();

            Assert.Contains("id=\"storeContextCard\"", view);
            Assert.Contains("id=\"storeContextBadge\"", view);
            Assert.Contains("Your workspace is ready", view);
            Assert.Contains("Store login detected", view);
            Assert.Contains("Auto-selected", view);
            Assert.Contains("Your store has been securely identified. Your workspace will open after successful authentication.", view);
            Assert.Contains("Verified store assignment", view);
            Assert.Contains("id=\"storeContextName\"", view);
            Assert.Contains("id=\"storeContextCode\"", view);
        }

        [Fact]
        public void LoginView_ShowsCardOnlyForSingleStoreAndClearsItForOtherModes()
        {
            var view = LoginViewContents();

            Assert.Contains("if (autoStore)", view);
            Assert.Contains("storeContextCard.addClass('is-visible')", view);
            Assert.Contains("storeContextCard.removeClass('is-visible')", view);
            Assert.Contains("if (stores.length === 1)", view);
            Assert.Contains("else if (stores.length > 1)", view);
            Assert.Contains("setLoginModeHint(true, '', false)", view);
            Assert.Contains("setLoginModeHint(false, \"\", true)", view);
        }

        [Fact]
        public void LoginView_UsesDynamicStoreValueAndDoesNotContainLegacyAutoSelectionText()
        {
            var view = LoginViewContents();

            Assert.Contains("storeContextName.text('Store ' + autoStore)", view);
            Assert.Contains("storeContextCode.text(autoStore)", view);
            Assert.DoesNotContain("Store-user login detected. Store auto-selected:", view);
        }

        [Fact]
        public void LoginView_StoreLookupRetryProtection_AllowsPasswordRetryWithoutDuplicateSpam()
        {
            var view = LoginViewContents();

            Assert.Contains("var tenantStoreLookupNeedsPasswordRetry = false;", view);
            Assert.Contains("normalizedTenantEmail === tenantStoreLookupLastCompletedEmail && !tenantStoreLookupNeedsPasswordRetry", view);
            Assert.Contains("tenantStoreLookupNeedsPasswordRetry = true;", view);
            Assert.Contains("$('#tenantPassword').on('change blur'", view);
            Assert.Contains("setLoginModeHint(true, \"\", false, 'empty')", view);
            Assert.Contains("setLoginModeHint(true, \"\", false, 'pending')", view);
            Assert.Contains("setLoginModeHint(true, \"\", false, 'multiple')", view);
            Assert.Contains("storeContextBadge.text('Auto-selected: ' + autoStore)", view);
            Assert.Contains("No active store assignment found for this account.", view);
            Assert.Contains("Checking your workspace...", view);
        }

        [Fact]
        public void LoginView_StoreLookupInputDebounce_IsSetToFastGuardedValue()
        {
            var view = LoginViewContents();

            Assert.Contains("}, 120);", view);
        }
    }
}