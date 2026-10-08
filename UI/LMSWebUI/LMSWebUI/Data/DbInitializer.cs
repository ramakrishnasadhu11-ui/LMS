using System;
using System.Threading.Tasks;
using LMSWebUI.Models;
using Microsoft.AspNetCore.Identity;

namespace LMSWebUI.Data
{
    public static class DbInitializer
    {
        public static async Task SeedAsync(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            // Ensure SuperAdmin role exists
            var superRole = "SuperAdmin";
            if (!await roleManager.RoleExistsAsync(superRole))
            {
                var role = new IdentityRole(superRole);
                await roleManager.CreateAsync(role);
            }

            // Create default super admin user if not exists
            var adminEmail = "admin@local";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                var user = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true
                };

                // NOTE: Change this password after first sign-in. Keep a copy for administrators.
                var adminPassword = "Adm1n!ChangeMe#2026";
                var result = await userManager.CreateAsync(user, adminPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, superRole);
                }
                else
                {
                    // For visibility during development, you could log failures here.
                    // In this code we silently return.
                }
            }
        }
    }
}
