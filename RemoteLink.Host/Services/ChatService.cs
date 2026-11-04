using Microsoft.EntityFrameworkCore;
using RemoteLink.Core.Models.Chat;
using RemoteLink.Core.Models.Chat.Responses;
using RemoteLink.Host.Data;

namespace RemoteLink.Host.Services
{
    public sealed class ChatService(RemoteLinkDbContext database, TrustedDeviceService trustedDevices, DeviceIdentityService identity)
    {
        private void EnsureTrusted(Guid deviceId)
        {
            if (deviceId == Guid.Empty || trustedDevices.GetDevice(deviceId) is not { IsTrusted: true })
            {
                throw new UnauthorizedAccessException("The remote device is not trusted.");
            }
        }

        public async Task<ChatConversationResponse> OpenConversationAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            EnsureTrusted(deviceId);
            
            ChatConversation? conversation = await database.ChatConversations.FirstOrDefaultAsync(c => c.RemoteDeviceId == deviceId, cancellationToken);

            if (conversation is null)
            {
                conversation = new ChatConversation
                {
                    Id = Guid.NewGuid(),
                    RemoteDeviceId = deviceId,
                    CreatedUtc = DateTime.UtcNow
                };
                
                database.ChatConversations.Add(conversation);

                try
                {
                    await database.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    database.Entry(conversation).State = EntityState.Detached;
                    
                    conversation = await database.ChatConversations.SingleAsync(c => c.RemoteDeviceId == deviceId, cancellationToken);
                }
            }
            
            return await GetConversationResponseAsync(conversation, cancellationToken);
        }

        public async Task<IReadOnlyList<ChatConversationResponse>> GetConversationsAsync(CancellationToken cancellationToken = default)
        {
            List<ChatConversation> conversations = await database.ChatConversations.AsNoTracking()
                .OrderByDescending(c => c.LastMessageUtc)
                .ToListAsync(cancellationToken);

            List<ChatConversationResponse> results = [];

            foreach (ChatConversation conversation in conversations)
            {
                if (trustedDevices.GetDevice(conversation.RemoteDeviceId) is not { IsTrusted: true })
                {
                    continue;
                }
                
                results.Add(await GetConversationResponseAsync(conversation, cancellationToken));
            }
            
            return results;
        }

        public async Task<ChatMessageResponse> QueueMessageAsync(Guid deviceId, string content, CancellationToken cancellationToken = default)
        {
            EnsureTrusted(deviceId);

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ArgumentException("A message cannot be empty.");
            }
            
            content = content.Trim();

            if (content.Length > 10000)
            {
                throw new ArgumentException("Messages cannot exceed 10,000 characters.");
            }

            await OpenConversationAsync(deviceId, cancellationToken);
            
            ChatConversation conversation = await database.ChatConversations.SingleAsync(c => c.RemoteDeviceId == deviceId, cancellationToken);
            
            DateTime now = DateTime.UtcNow;

            ChatMessage message = new()
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                SenderDeviceId = identity.DeviceId,
                RecipientDeviceId = deviceId,
                Content = content,
                Direction = ChatMessageDirection.Outgoing,
                Status = ChatMessageStatus.Pending,
                CreatedUtc = now
            };
            
            database.ChatMessages.Add(message);
            
            conversation.LastMessageUtc = now;

            await database.SaveChangesAsync(cancellationToken);
            
            return ToResponse(message);
        }

        public async Task<IReadOnlyList<ChatMessageResponse>> GetMessagesAsync(Guid deviceId, DateTime? beforeUtc = null, Guid? beforeId = null, int limit = 50, CancellationToken cancellationToken = default)
        {
            EnsureTrusted(deviceId);

            if (limit < 1 || limit > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(limit), "Page size must be between 1 and 100.");
            }

            if (beforeUtc.HasValue != beforeId.HasValue)
            {
                throw new ArgumentException("Both beforeUtc and beforeId must be provided for pagination.");
            }
            
            ChatConversation? conversation = await database.ChatConversations.AsNoTracking().FirstOrDefaultAsync(c => c.RemoteDeviceId == deviceId, cancellationToken);

            if (conversation is null)
            {
                return [];
            }
            
            IQueryable<ChatMessage> query = database.ChatMessages.AsNoTracking().Where(m => m.ConversationId == conversation.Id);

            if (beforeUtc.HasValue && beforeId.HasValue)
            {
                DateTime cursorTime = beforeUtc.Value;
                Guid cursorId = beforeId.Value;
                
                query = query.Where(m => m.CreatedUtc < cursorTime || (m.CreatedUtc == cursorTime && m.Id.CompareTo(cursorId) < 0));
            }
            
            List<ChatMessage> messages = await query.OrderByDescending(m => m.CreatedUtc)
                .ThenByDescending(m => m.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
            
            messages.Reverse();

            return messages.Select(ToResponse).ToList();
        }

        public async Task<IReadOnlyList<ChatMessageResponse>> GetPendingMessagesAsync(CancellationToken cancellationToken = default)
        {
            List<ChatMessage> messages = await database.ChatMessages.AsNoTracking()
                .Where(m => m.Direction == ChatMessageDirection.Outgoing && m.Status == ChatMessageStatus.Pending)
                .OrderBy(m => m.CreatedUtc)
                .ThenBy(m => m.Id)
                .Take(100)
                .ToListAsync(cancellationToken);

            return messages.Where(m => trustedDevices.GetDevice(m.RecipientDeviceId) is { IsTrusted: true })
                .Select(ToResponse)
                .ToList();
        }

        private async Task<ChatConversationResponse> GetConversationResponseAsync(ChatConversation conversation, CancellationToken cancellationToken)
        {
            ChatMessage? latest = await database.ChatMessages.AsNoTracking()
                .Where(m => m.ConversationId == conversation.Id)
                .OrderByDescending(m => m.CreatedUtc)
                .ThenByDescending(m => m.Id)
                .FirstOrDefaultAsync(cancellationToken);
            
            int unread = await database.ChatMessages.AsNoTracking()
                .CountAsync(m => m.ConversationId == conversation.Id && m.Direction == ChatMessageDirection.Incoming && m.ReadUtc == null, cancellationToken);

            return new ChatConversationResponse
            {
                Id = conversation.Id,
                RemoteDeviceId = conversation.RemoteDeviceId,
                RemoteDeviceName = trustedDevices.GetDevice(conversation.RemoteDeviceId)?.DeviceName ?? "Unknown Device",
                CreatedUtc = conversation.CreatedUtc,
                LastMessageUtc = conversation.LastMessageUtc,
                LastReadUtc = conversation.LastReadUtc,
                LastMessagePreview = latest is null ? null : latest.Content.Length > 100 ? latest.Content[..100] + "..." : latest.Content,
                UnreadCount = unread
            };
        }

        private static ChatMessageResponse ToResponse(ChatMessage message)
        {
            return new ChatMessageResponse
            {
                Id = message.Id,
                ConversationId = message.ConversationId,
                SenderDeviceId = message.SenderDeviceId,
                RecipientDeviceId = message.RecipientDeviceId,
                Content = message.Content,
                Direction = message.Direction,
                Status = message.Status,
                CreatedUtc = message.CreatedUtc,
                SentUtc = message.SentUtc,
                DeliveredUtc = message.DeliveredUtc,
                ReadUtc = message.ReadUtc
            };
        }
    }
}