using System;

namespace MPMS.Models
{
    public class Notification
    {
        public int NotificationId { get; set; }
        public int ReceiverUserId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? RefType { get; set; }
        public int? RefId { get; set; }
        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
