using LMS.Identity.Utilities;
using LMS.Identity.DTO;
using System.Threading.Tasks;
using System.Security.Claims;

namespace LMS.Identity.BusinessSerive.Interfaces
{
    public interface IRazorpayIntegrationService
    {
        bool IsLiveMode { get; }

        string ActiveKeyId { get; }

        Task<ActionReturnType> CreateTenantSubscriptionPaymentOrder(CreateTenantSubscriptionPaymentOrderRequestDto request, ClaimsPrincipal user);

        Task<ActionReturnType> VerifyTenantSubscriptionCheckoutPayment(VerifyCheckoutPaymentRequestDto request, ClaimsPrincipal user);

        Task<ActionReturnType> ReconcileCapturedPayment(ReconcileCapturedPaymentRequestDto request);

        Task<ActionReturnType> ProcessRazorpayWebhook(string rawPayload, string razorpaySignature);

        Task<ActionReturnType> GetTenantSubscriptionPaymentHistory(ClaimsPrincipal user);

        Task<ActionReturnType> GetTenantSubscriptionOverview(ClaimsPrincipal user);
    }
}
