using System.Threading.Tasks;

namespace LMS.Identity.BusinessSerive.Interfaces
{
    public interface ITenantEmailService
    {
        Task SendMailAsync(string email, string subject, string body, string tenantId = null, string tenantEmail = null);
    }
}