using System;
using System.Linq;
using LMS.Identity.BusinessSerive.Common;
using LMS.Identity.DTO.Entities;
using Microsoft.Extensions.Configuration;

namespace LMS.Identity.BusinessSerive.Data
{
    /// <summary>
    /// Seeds the initial super admin account from configuration so that at least one
    /// global administrator exists before any tenant is registered.
    /// </summary>
    public static class SuperAdminSeeder
    {
        public static void Seed(IdentityDbContext dbContext, IConfiguration configuration)
        {
            if (dbContext == null || configuration == null)
            {
                return;
            }

            var email = configuration["SuperAdmin:Email"];
            var password = configuration["SuperAdmin:Password"];
            var displayName = configuration["SuperAdmin:DisplayName"];

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return;
            }

            var normalizedEmail = email.Trim();

            // Only seed when the configured account does not already exist. Existing
            // accounts are never overwritten so a changed password is preserved.
            var exists = dbContext.SuperAdmins.Any(x => x.Email == normalizedEmail);
            if (exists)
            {
                return;
            }

            dbContext.SuperAdmins.Add(new SuperAdminEntity
            {
                Email = normalizedEmail,
                Password = PasswordHasher.Hash(password),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Super Admin" : displayName.Trim(),
                IsActive = true,
                IsPasswordChanged = false,
                CreatedDate = DateTime.UtcNow
            });

            dbContext.SaveChanges();
        }
    }
}
