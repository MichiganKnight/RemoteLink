namespace RemoteLink.Core.Models.Chat.Requests
{
    public sealed class ReceiveChatMessageRequest
    {
        public Guid MessageId { get; set; }

        public Guid SenderDeviceId { get; set; }

        public Guid RecipientDeviceId { get; set; }

        public string Content { get; set; } = string.Empty;

        public DateTime CreatedUtc { get; set; }
    }
}