using System;
using System.Threading.Tasks;
using LMS.Identity.BusinessSerive.Services;

namespace LMS.Identity.BusinessSerive.Tests.Fixtures
{
    // A test mail configuration that simulates failures by throwing on Send.
    public class FailingSmtpMailConfiguration : IMailConfiguration
    {
        public string MailSubject { get; set; } = "Test";
        public string MailFrom { get; set; } = "noreply@example.com";
        public string Sender { get; set; } = string.Empty;
        public string SmtpServer { get; set; } = "invalid";
        public string Reciever { get; set; } = string.Empty;
        public int Port { get; set; } = 465;
        public string Username { get; set; } = "invalid";
        public string Password { get; set; } = "invalid";
        public string AlertMailSubject { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string SuccessMessage { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }
}
