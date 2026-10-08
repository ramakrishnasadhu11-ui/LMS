using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace LMSWebUI.Models.Admin
{
    public class WorkflowStatusItemViewModel
    {
        [Display(Name = "Status Name")]
        [Required(ErrorMessage = "Status name is required.")]
        [StringLength(64, ErrorMessage = "Status name cannot exceed 64 characters.")]
        public string StatusName { get; set; }

        [Display(Name = "Order")]
        [Range(1, 999, ErrorMessage = "Order must be between 1 and 999.")]
        public int SortOrder { get; set; } = 1;

        [Display(Name = "Colour")]
        [StringLength(16, ErrorMessage = "Colour cannot exceed 16 characters.")]
        public string ColorCode { get; set; } = "#6f7a86";

        [Display(Name = "Default For New Orders")]
        public bool IsDefault { get; set; }

        [Display(Name = "Final Status")]
        public bool IsFinal { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;
    }

    public class WorkflowStatusSettingsViewModel
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }

        /// <summary>
        /// Registered company name of the tenant. Display only; never persisted with the settings.
        /// </summary>
        public string CompanyName { get; set; }

        public List<WorkflowStatusItemViewModel> Statuses { get; set; } = new List<WorkflowStatusItemViewModel>();

        /// <summary>
        /// Standard laundry lifecycle used when a store has not configured its own statuses yet.
        /// </summary>
        public static List<WorkflowStatusItemViewModel> GetSeedStatuses()
        {
            return new List<WorkflowStatusItemViewModel>
            {
                new WorkflowStatusItemViewModel { StatusName = "Received", SortOrder = 1, ColorCode = "#2563eb", IsDefault = true, IsFinal = false, IsActive = true },
                new WorkflowStatusItemViewModel { StatusName = "In Process", SortOrder = 2, ColorCode = "#d97706", IsDefault = false, IsFinal = false, IsActive = true },
                new WorkflowStatusItemViewModel { StatusName = "Ready for Delivery", SortOrder = 3, ColorCode = "#7c3aed", IsDefault = false, IsFinal = false, IsActive = true },
                new WorkflowStatusItemViewModel { StatusName = "Delivered", SortOrder = 4, ColorCode = "#2f8f46", IsDefault = false, IsFinal = true, IsActive = true },
                new WorkflowStatusItemViewModel { StatusName = "Cancelled", SortOrder = 5, ColorCode = "#b63838", IsDefault = false, IsFinal = true, IsActive = true }
            };
        }
    }
}
