using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RemoteLink.Core.Models.Chat;
using RemoteLink.Core.Models.Chat.Requests;
using RemoteLink.Core.Models.Chat.Responses;
using RemoteLink.Host.Data;

namespace RemoteLink.Host.Services
{
    public sealed class ChatService(RemoteLinkDbContext database, TrustedDeviceService trustedDevices, DeviceIdentityService identity, ChatNotificationService notifications)
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

            await notifications.NotifyConversationChangedAsync(deviceId, cancellationToken);
            
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

        public async Task<ChatDeliveryAcknowledgment> ReceiveMessageAsync(Guid authenticatedDeviceId, ReceiveChatMessageRequest request, CancellationToken cancellationToken = default)
        {
            EnsureTrusted(authenticatedDeviceId);

            if (request.MessageId == Guid.Empty || request.SenderDeviceId != authenticatedDeviceId || request.RecipientDeviceId != identity.DeviceId)
            {
                throw new ArgumentException("Invalid message identifiers.");
            }

            if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 10000)
            {
                throw new ArgumentException("Invalid message content.");
            }

            if (request.CreatedUtc.Kind != DateTimeKind.Utc || request.CreatedUtc > DateTime.UtcNow.AddMinutes(5))
            {
                throw new ArgumentException("Invalid message timestamp.");
            }
            
            ChatMessage? existing = await database.ChatMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.MessageId, cancellationToken);

            if (existing is not null)
            {
                if (existing.Direction != ChatMessageDirection.Incoming || existing.SenderDeviceId != authenticatedDeviceId || existing.RecipientDeviceId != identity.DeviceId || existing.Content != request.Content || existing.CreatedUtc != request.CreatedUtc)
                {
                    throw new InvalidOperationException("The message ID is already associated with different content.");
                }

                return new ChatDeliveryAcknowledgment
                {
                    MessageId = existing.Id,
                    ReceivingDeviceId = identity.DeviceId,
                    DeliveredUtc = existing.DeliveredUtc ?? existing.CreatedUtc
                };
            }

            await OpenConversationAsync(authenticatedDeviceId, cancellationToken);

            ChatConversation conversation = await database.ChatConversations.SingleAsync(c => c.RemoteDeviceId == authenticatedDeviceId, cancellationToken);

            DateTime now = DateTime.UtcNow;

            ChatMessage message = new()
            {
                Id = request.MessageId,
                ConversationId = conversation.Id,
                SenderDeviceId = authenticatedDeviceId,
                RecipientDeviceId = identity.DeviceId,
                Content = request.Content,
                Direction = ChatMessageDirection.Incoming,
                Status = ChatMessageStatus.Delivered,
                CreatedUtc = request.CreatedUtc,
                DeliveredUtc = now
            };

            database.ChatMessages.Add(message);

            if (conversation.LastMessageUtc is null || conversation.LastMessageUtc < now)
            {
                conversation.LastMessageUtc = now;
            }

            try
            {
                await database.SaveChangesAsync(cancellationToken);
                
                await notifications.NotifyConversationChangedAsync(authenticatedDeviceId, cancellationToken);
            }
            catch (DbUpdateException exception)when (exception.InnerException is SqliteException
                                                     {
                                                         SqliteErrorCode: 19
                                                     })
            {
                database.Entry(message).State = EntityState.Detached;
                database.Entry(conversation).State = EntityState.Detached;

                ChatMessage? duplicate = await database.ChatMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.MessageId, cancellationToken);

                if (duplicate is null || duplicate.Direction != ChatMessageDirection.Incoming || duplicate.SenderDeviceId != authenticatedDeviceId || duplicate.RecipientDeviceId != identity.DeviceId ||
                    duplicate.Content != request.Content || duplicate.CreatedUtc != request.CreatedUtc)
                {
                    throw;
                }

                return new ChatDeliveryAcknowledgment
                {
                    MessageId = duplicate.Id,
                    ReceivingDeviceId = identity.DeviceId,
                    DeliveredUtc = duplicate.DeliveredUtc ?? duplicate.CreatedUtc
                };
            }

            return new ChatDeliveryAcknowledgment
            {
                MessageId = message.Id,
                ReceivingDeviceId = identity.DeviceId,
                DeliveredUtc = now
            };
        }

        public async Task<bool> MarkMessageDeliveredAsync(Guid messageId, Guid remoteDeviceId, DateTime deliveredUtc, CancellationToken cancellationToken = default)
        {
            EnsureTrusted(remoteDeviceId);

            ChatMessage? message = await database.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.RecipientDeviceId == remoteDeviceId && m.Direction == ChatMessageDirection.Outgoing, cancellationToken);

            if (message is null)
            {
                return false;
            }

            if (message.Status == ChatMessageStatus.Delivered || message.Status == ChatMessageStatus.Read)
            {
                return true;
            }

            if (message.Status != ChatMessageStatus.Pending && message.Status != ChatMessageStatus.Sent)
            {
                return false;
            }

            DateTime now = DateTime.UtcNow;

            message.Status = ChatMessageStatus.Delivered;
            message.SentUtc ??= now;
            message.DeliveredUtc = deliveredUtc;

            await database.SaveChangesAsync(cancellationToken);
            
            await notifications.NotifyConversationChangedAsync(remoteDeviceId, cancellationToken);

            return true;
        }
        
        public async Task<int> MarkConversationReadAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            EnsureTrusted(deviceId);

            ChatConversation? conversation = await database.ChatConversations.FirstOrDefaultAsync(c => c.RemoteDeviceId == deviceId, cancellationToken);

            if (conversation is null)
            {
                return 0;
            }

            DateTime now = DateTime.UtcNow;

            int updated = await database.ChatMessages.Where(m => m.ConversationId == conversation.Id && m.Direction == ChatMessageDirection.Incoming && m.ReadUtc == null).ExecuteUpdateAsync(setters => setters.SetProperty(m => m.ReadUtc, now).SetProperty(m => m.Status, ChatMessageStatus.Read), cancellationToken);

            if (updated > 0)
            {
                conversation.LastReadUtc = now;

                await database.SaveChangesAsync(cancellationToken);
            
                await notifications.NotifyConversationChangedAsync(deviceId, cancellationToken);
            }

            return updated;
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
                CreatedUtc = AsUtc(message.CreatedUtc),
                SentUtc = AsUtc(message.SentUtc),
                DeliveredUtc = AsUtc(message.DeliveredUtc),
                ReadUtc = AsUtc(message.ReadUtc)
            };
        }

        private static DateTime AsUtc(DateTime value)
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static DateTime? AsUtc(DateTime? value)
        {
            return value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
        }
    }
}