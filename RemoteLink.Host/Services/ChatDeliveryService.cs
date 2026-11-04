using RemoteLink.Core.Models.Chat.Requests;
using RemoteLink.Core.Models.Chat.Responses;

namespace RemoteLink.Host.Services
{
    public sealed class ChatDeliveryService(ChatService chat, TrustedPeerClientService peerClient)
    {
        public async Task<ChatDeliveryAcknowledgment> DeliverAsync(Guid remoteDeviceId, string remoteAddress, Guid messageId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ChatMessageResponse> pending = await chat.GetPendingMessagesAsync(cancellationToken);

            ChatMessageResponse message = pending.FirstOrDefault(m => m.Id == messageId && m.RecipientDeviceId == remoteDeviceId) ?? throw new InvalidOperationException("The requested message is not pending delivery.");

            using AuthenticatedPeerConnection connection = await peerClient.ConnectAsync(remoteDeviceId, remoteAddress, cancellationToken);

            ReceiveChatMessageRequest request = new()
            {
                MessageId = message.Id,
                SenderDeviceId = message.SenderDeviceId,
                RecipientDeviceId = message.RecipientDeviceId,
                Content = message.Content,
                CreatedUtc = DateTime.SpecifyKind(message.CreatedUtc, DateTimeKind.Utc)
            };

            ChatDeliveryAcknowledgment acknowledgment = await connection.SendMessageAsync(request, cancellationToken);

            bool updated = await chat.MarkMessageDeliveredAsync(message.Id, remoteDeviceId, acknowledgment.DeliveredUtc, cancellationToken);

            if (!updated)
            {
                throw new InvalidOperationException("The message was delivered but its local status could not be updated.");
            }

            return acknowledgment;
        }
    }
}