namespace RemoteLink.Core.Models.Chat.Responses
{
    public sealed class ChatConversationResponse
    {
        public Guid Id { get; set; }

        public Guid RemoteDeviceId { get; set; }

        public string RemoteDeviceName { get; set; } = string.Empty;

        public DateTime CreatedUtc { get; set; }

        public DateTime? LastMessageUtc { get; set; }

        public DateTime? LastReadUtc { get; set; }

        public string? LastMessagePreview { get; set; }

        public int UnreadCount { get; set; }
    }
}