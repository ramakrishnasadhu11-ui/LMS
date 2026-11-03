using System;

namespace LMS.Identity.BusinessSerive.Services
{
    public class RazorpayOptions : IRazorpayOptions
    {
        public string Mode { get; set; } = "Test";

        public RazorpayCredentialOptions Test { get; set; } = new RazorpayCredentialOptions();

        public RazorpayCredentialOptions Live { get; set; } = new RazorpayCredentialOptions();

        public bool IsLiveMode => string.Equals(Mode, "Live", StringComparison.OrdinalIgnoreCase);

        public RazorpayCredentialOptions GetActiveCredentials()
        {
            return IsLiveMode ? Live : Test;
        }
    }

    public class RazorpayCredentialOptions
    {
        public string KeyId { get; set; }

        public string KeySecret { get; set; }

        public string WebhookSecret { get; set; }
    }

    public interface IRazorpayOptions
    {
        string Mode { get; set; }

        RazorpayCredentialOptions Test { get; set; }

        RazorpayCredentialOptions Live { get; set; }

        bool IsLiveMode { get; }

        RazorpayCredentialOptions GetActiveCredentials();
    }
}
