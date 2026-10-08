using System.Collections.Generic;

namespace LMS.Master.DTO
{
    /// <summary>
    /// A single order lifecycle status configured for a store.
    /// </summary>
    public class WorkflowStatusDto
    {
        public string StatusName { get; set; }
        public int SortOrder { get; set; }
        public string ColorCode { get; set; }
        public bool IsDefault { get; set; }
        public bool IsFinal { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Full workflow status configuration for a single tenant store.
    /// </summary>
    public class WorkflowStatusSettingsDto
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public List<WorkflowStatusDto> Statuses { get; set; } = new List<WorkflowStatusDto>();
    }
}
