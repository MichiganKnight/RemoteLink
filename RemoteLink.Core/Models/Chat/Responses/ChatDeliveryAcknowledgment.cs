namespace RemoteLink.Core.Models.Chat.Responses
{
    public sealed class ChatDeliveryAcknowledgment
    {
        public Guid MessageId { get; set; }

        public Guid ReceivingDeviceId { get; set; }

        public DateTime DeliveredUtc { get; set; }
    }
}