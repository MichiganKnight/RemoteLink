using Microsoft.EntityFrameworkCore;
using RemoteLink.Core.Models.Transfers;
using RemoteLink.Host.Data;

namespace RemoteLink.Host.Services
{
    public sealed class TransferCoordinator(IServiceScopeFactory scopeFactory, TransferSourceRegistry sources, ILogger<TransferCoordinator> logger) : BackgroundService
    {
        private readonly HashSet<Guid> _activeTransfers = [];
        private readonly object _sync = new();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Transfer coordinator started.");

            using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));

            try
            {
                await CheckTransfersAsync(stoppingToken);

                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await CheckTransfersAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal application shutdown.
            }
        }

        private async Task CheckTransfersAsync(CancellationToken cancellationToken)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

                RemoteLinkDbContext database = scope.ServiceProvider.GetRequiredService<RemoteLinkDbContext>();

                List<Guid> acceptedTransfers = await database.TransferJobs.AsNoTracking()
                    .Where(transfer => transfer.Direction == TransferDirection.Outgoing && transfer.Status == TransferStatus.Accepted)
                    .Select(transfer => transfer.Id)
                    .ToListAsync(cancellationToken);

                foreach (Guid transferId in acceptedTransfers)
                {
                    lock (_sync)
                    {
                        if (!_activeTransfers.Add(transferId))
                        {
                            continue;
                        }
                    }

                    _ = ProcessTransferAsync(transferId, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unable to check pending transfers.");
            }
        }

        private async Task ProcessTransferAsync(Guid transferId, CancellationToken cancellationToken)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

                RemoteLinkDbContext database = scope.ServiceProvider.GetRequiredService<RemoteLinkDbContext>();

                TrustedPeerClientService peerClient = scope.ServiceProvider.GetRequiredService<TrustedPeerClientService>();

                ChatPeerAddressResolver addresses = scope.ServiceProvider.GetRequiredService<ChatPeerAddressResolver>();

                TransferJob? transfer = await database.TransferJobs.Include(t => t.Items)
                    .FirstOrDefaultAsync(t => t.Id == transferId, cancellationToken);

                if (transfer is null || transfer.Direction != TransferDirection.Outgoing || transfer.Status != TransferStatus.Accepted)
                {
                    return;
                }

                List<TransferItem> files = transfer.Items.Where(item => !item.IsDirectory).ToList();
                
                if (files.Count != 1 || transfer.Items.Count != 1)
                {
                    logger.LogWarning("Transfer {TransferId} is not a single-file transfer.", transferId);

                    return;
                }

                TransferItem item = files[0];

                string sourcePath;

                try
                {
                    sourcePath = sources.Resolve(item.Id);
                }
                catch (FileNotFoundException)
                {
                    logger.LogWarning("Source unavailable for transfer {TransferId}.", transferId);

                    return;
                }

                FileInfo sourceFile = new(sourcePath);

                if (!sourceFile.Exists || sourceFile.Length != item.SizeBytes)
                {
                    throw new IOException("Source file is missing or its size has changed.");
                }

                string address = addresses.Resolve(transfer.RemoteDeviceId) ?? throw new InvalidOperationException("Recipient is not currently discovered on LAN.");

                transfer.Status = TransferStatus.Transferring;
                transfer.StartedUtc ??= DateTime.UtcNow;
                transfer.ErrorMessage = null;

                await database.SaveChangesAsync(cancellationToken);

                logger.LogInformation("Starting transfer {TransferId}: {FileName}", transfer.Id, item.RelativePath);

                using AuthenticatedPeerConnection connection = await peerClient.ConnectAsync(transfer.RemoteDeviceId, address, cancellationToken);

                await connection.SendTransferFileAsync(transfer.Id, item.Id, sourcePath, cancellationToken);
                
                item.TransferredBytes = item.SizeBytes;
                transfer.TransferredBytes = item.SizeBytes;
                transfer.Status = TransferStatus.Completed;
                transfer.CompletedUtc = DateTime.UtcNow;

                await database.SaveChangesAsync(cancellationToken);

                sources.Remove(item.Id);

                logger.LogInformation("Transfer {TransferId} completed successfully.", transfer.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Transfer {TransferId} interrupted by shutdown.", transferId);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Transfer {TransferId} failed.", transferId);

                try
                {
                    await MarkFailedAsync(transferId, exception);
                }
                catch (Exception saveException)
                {
                    logger.LogError(saveException, "Unable to save failure for transfer {TransferId}.", transferId);
                }
            }
            finally
            {
                lock (_sync)
                {
                    _activeTransfers.Remove(transferId);
                }
            }
        }

        private async Task MarkFailedAsync(Guid transferId, Exception exception)
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

            RemoteLinkDbContext database = scope.ServiceProvider.GetRequiredService<RemoteLinkDbContext>();

            TransferJob? transfer = await database.TransferJobs.FirstOrDefaultAsync(t => t.Id == transferId);

            if (transfer is null || transfer.Status == TransferStatus.Completed)
            {
                return;
            }

            transfer.Status = TransferStatus.Failed;
            transfer.ErrorMessage = exception.Message.Length > 2048 ? exception.Message[..2048] : exception.Message;

            await database.SaveChangesAsync();
        }
    }
}