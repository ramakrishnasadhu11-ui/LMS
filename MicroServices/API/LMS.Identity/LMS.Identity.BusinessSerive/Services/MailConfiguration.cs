using System;

namespace LMS.Identity.BusinessSerive.Services
{
    public class MailConfiguration : IMailConfiguration
    {
        public string MailSubject { get; set; }
        public string MailFrom { get; set; }
        public string Sender { get; set; }
        public string SmtpServer { get; set; }
        public string Reciever { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string AlertMailSubject { get; set; }
        public string ErrorMessage { get; set; }
        public string SuccessMessage { get; set; }
        public string ApiKey { get; set; }
    }

    public interface IMailConfiguration
    {
        string MailSubject { get; set; }
        string MailFrom { get; set; }
        string Sender { get; set; }
        string SmtpServer { get; set; }
        string Reciever { get; set; }
        int Port { get; set; }
        string Username { get; set; }
        string Password { get; set; }
        string AlertMailSubject { get; set; }
        string ErrorMessage { get; set; }
        string SuccessMessage { get; set; }
        string ApiKey { get; set; }
    }
}
