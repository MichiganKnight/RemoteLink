using Microsoft.AspNetCore.SignalR;
using RemoteLink.Host.Hubs;

namespace RemoteLink.Host.Services
{
    public sealed class ChatNotificationService(IHubContext<LocalChatHub> hub)
    {
        public Task NotifyConversationChangedAsync(Guid remoteDeviceId, CancellationToken cancellationToken = default)
        {
            return hub.Clients.All.SendAsync("ConversationChanged", new
            {
                deviceId = remoteDeviceId
            }, cancellationToken);
        }   
    }
}