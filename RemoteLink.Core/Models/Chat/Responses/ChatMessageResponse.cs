namespace RemoteLink.Core.Models.Chat.Responses
{
    public sealed class ChatMessageResponse
    {
        public Guid Id { get; set; }

        public Guid ConversationId { get; set; }

        public Guid SenderDeviceId { get; set; }

        public Guid RecipientDeviceId { get; set; }

        public string Content { get; set; } = string.Empty;

        public ChatMessageDirection Direction { get; set; }

        public ChatMessageStatus Status { get; set; }

        public DateTime CreatedUtc { get; set; }

        public DateTime? SentUtc { get; set; }

        public DateTime? DeliveredUtc { get; set; }

        public DateTime? ReadUtc { get; set; }
    }
}