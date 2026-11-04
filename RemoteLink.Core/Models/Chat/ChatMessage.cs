namespace RemoteLink.Core.Models.Chat
{
    public sealed class ChatMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public Guid ConversationId { get; set; }
        
        public Guid SenderDeviceId { get; set; }
        
        public Guid RecipientDeviceId { get; set; }
        
        public string Content { get; set; } = string.Empty;
        
        public ChatMessageDirection Direction { get; set; }
        
        public ChatMessageStatus Status { get; set; } = ChatMessageStatus.Pending;
        
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        
        public DateTime? SentUtc { get; set; }
        
        public DateTime? DeliveredUtc { get; set; }
        
        public DateTime? ReadUtc { get; set; }

        public ChatConversation Conversation { get; set; } = null!;
    }
}