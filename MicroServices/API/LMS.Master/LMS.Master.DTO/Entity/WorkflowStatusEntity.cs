using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Master.DTO.Entity
{
    /// <summary>
    /// A single order lifecycle status configured for a store.
    /// </summary>
    public class WorkflowStatusEntity
    {
        [Key]
        public int Id { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string StatusName { get; set; }

        /// <summary>
        /// Position of the status in the lifecycle. Lower values come first.
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// Hex colour used to badge the status in the UI, for example "#2f8f46".
        /// </summary>
        public string ColorCode { get; set; }

        /// <summary>
        /// The status applied to newly created orders. Only one per store.
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// Marks a terminal status such as Delivered or Cancelled.
        /// </summary>
        public bool IsFinal { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }
    }
}
