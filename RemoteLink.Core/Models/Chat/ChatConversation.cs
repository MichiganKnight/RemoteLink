namespace RemoteLink.Core.Models.Chat
{
    public sealed class ChatConversation
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public Guid RemoteDeviceId { get; set; }
        
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        
        public DateTime? LastMessageUtc { get; set; }
        
        public DateTime? LastReadUtc { get; set; }

        public ICollection<ChatMessage> Messages { get; set; } = [];
    }
}