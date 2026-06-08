using System;

namespace MPMS.Models
{
    public class TaskDependency
    {
        public int DependencyId { get; set; }
        public int TaskId { get; set; }
        public int PredecessorTaskId { get; set; }
        public string DependencyType { get; set; } = "FS"; // Finish-to-Start
        public DateTime CreatedAt { get; set; }

        // Extra info fields
        public string TaskTitle { get; set; } = string.Empty;
        public string PredecessorTaskTitle { get; set; } = string.Empty;
    }
}
