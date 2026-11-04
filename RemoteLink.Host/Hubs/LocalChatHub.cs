using Microsoft.AspNetCore.SignalR;

namespace RemoteLink.Host.Hubs
{
    public sealed class LocalChatHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            if (Context.GetHttpContext()?.Connection.LocalPort != 5017)
            {
                Context.Abort();
                
                return;
            }
            
            await base.OnConnectedAsync();
        }
    }
}