using Microsoft.AspNetCore.SignalR;

namespace MPMS.Hubs
{
    public class NotificationHub : Hub
    {
        // Hub for routing real-time in-app notifications to users.
        // User identity is automatically matched via claims name identifier.
    }
}
