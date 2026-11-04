using RemoteLink.Core.Models.Chat.Responses;

namespace RemoteLink.Host.Services
{
    public sealed class ChatDeliveryWorker(IServiceScopeFactory scopeFactory, ChatPeerAddressResolver addresses, ILogger<ChatDeliveryWorker> logger) : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

        private readonly Dictionary<Guid, DateTime> _retryAfter = new();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using PeriodicTimer timer = new(PollInterval);

            try
            {
                await ProcessPendingAsync(stoppingToken);

                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await ProcessPendingAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
            {
                // Normal application shutdown.
            }
        }

        private async Task ProcessPendingAsync(CancellationToken cancellationToken)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();

                ChatService chat = scope.ServiceProvider.GetRequiredService<ChatService>();

                ChatDeliveryService delivery = scope.ServiceProvider.GetRequiredService<ChatDeliveryService>();

                IReadOnlyList<ChatMessageResponse> pending = await chat.GetPendingMessagesAsync(cancellationToken);

                foreach (ChatMessageResponse message in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_retryAfter.TryGetValue(message.RecipientDeviceId, out DateTime retryTime) && DateTime.UtcNow < retryTime)
                    {
                        continue;
                    }

                    string? address = addresses.Resolve(message.RecipientDeviceId);

                    if (string.IsNullOrWhiteSpace(address))
                    {
                        continue;
                    }

                    try
                    {
                        await delivery.DeliverAsync(message.RecipientDeviceId, address, message.Id, cancellationToken);

                        _retryAfter.Remove(message.RecipientDeviceId);

                        logger.LogInformation("Delivered chat message {MessageId} to {DeviceId}", message.Id, message.RecipientDeviceId);
                    }
                    catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _retryAfter[message.RecipientDeviceId] = DateTime.UtcNow.AddSeconds(15);

                        logger.LogWarning(exception, "Chat delivery failed for device {DeviceId}", message.RecipientDeviceId);
                    }
                }
            }
            catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected error processing pending chat messages.");
            }
        }
    }
}