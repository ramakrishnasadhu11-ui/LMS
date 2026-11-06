using LMS.Master.DTO.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LMS.Master.WebApi.Tests
{
    internal sealed class CreateLaundryOrderRollbackFailureInterceptor : SaveChangesInterceptor
    {
        private readonly string _tenantName;
        private readonly string _storeCode;
        private readonly string _custCode;
        private bool _hasThrown;

        public CreateLaundryOrderRollbackFailureInterceptor(string tenantName, string storeCode, string custCode)
        {
            _tenantName = tenantName?.Trim() ?? string.Empty;
            _storeCode = storeCode?.Trim() ?? string.Empty;
            _custCode = custCode?.Trim() ?? string.Empty;
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            TryThrow(eventData?.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            TryThrow(eventData?.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void TryThrow(DbContext context)
        {
            if (_hasThrown || context == null)
            {
                return;
            }

            var hasTargetTaxLinesPending = context.ChangeTracker
                .Entries<LaundryOrderTaxLineEntity>()
                .Any(e => e.State == EntityState.Added
                          && string.Equals(e.Entity.TenantName, _tenantName, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(e.Entity.StoreCode, _storeCode, StringComparison.OrdinalIgnoreCase));

            if (!hasTargetTaxLinesPending)
            {
                return;
            }

            var hasSavedTargetOrderInContext = context.ChangeTracker
                .Entries<LaundryOrderEntity>()
                .Any(e => e.State == EntityState.Unchanged
                          && string.Equals(e.Entity.TenantName, _tenantName, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(e.Entity.StoreCode, _storeCode, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(e.Entity.CustCode, _custCode, StringComparison.OrdinalIgnoreCase)
                          && !string.IsNullOrWhiteSpace(e.Entity.OrderNo));

            if (!hasSavedTargetOrderInContext)
            {
                return;
            }

            _hasThrown = true;
            throw new InvalidOperationException("TEST_INJECTED_CREATE_ORDER_AFTER_ORDER_SAVE_BEFORE_TX_COMMIT");
        }
    }
}