namespace LMS.Identity.DTO.Entities
{
    /// <summary>
    /// Approval states a tenant can be in. Stored as a string on <see cref="TenantEntity.ApprovalStatus"/>
    /// so the value stays readable in the database.
    /// </summary>
    public static class TenantApprovalStatus
    {
        public const string Pending = "Pending";

        public const string Approved = "Approved";

        public const string Rejected = "Rejected";

        /// <summary>
        /// Tenants created before the approval workflow existed have a null status and are
        /// treated as approved so they are never locked out.
        /// </summary>
        public static bool IsApproved(string status)
        {
            return string.IsNullOrWhiteSpace(status)
                || string.Equals(status, Approved, System.StringComparison.OrdinalIgnoreCase);
        }

        public static string Normalize(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return Approved;
            }

            if (string.Equals(status, Pending, System.StringComparison.OrdinalIgnoreCase))
            {
                return Pending;
            }

            if (string.Equals(status, Rejected, System.StringComparison.OrdinalIgnoreCase))
            {
                return Rejected;
            }

            return Approved;
        }
    }
}
