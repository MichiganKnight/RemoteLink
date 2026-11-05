using Microsoft.EntityFrameworkCore;
using RemoteLink.Core.Models.Transfers;
using RemoteLink.Host.Data;

namespace RemoteLink.Host.Services
{
    public sealed class TransferCoordinator(IServiceScopeFactory scopeFactory, TransferSourceRegistry sources, ILogger<TransferCoordinator> logger) : BackgroundService
    {
        private readonly HashSet<Guid> _activeTransfers = [];
        private readonly object _sync = new();
        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);

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
                
                string sourceHash = await TransferStorageService.CalculateSha256Async(sourcePath, cancellationToken);

                long uploadedBytes = 0;

                using CancellationTokenSource progressCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                
                Task progressTask = Task.Run(async () =>
                {
                    using PeriodicTimer progressTimer = new(ProgressInterval);

                    try
                    {
                        while (await progressTimer.WaitForNextTickAsync(progressCancellation.Token))
                        {
                            long bytes = Interlocked.Read(ref uploadedBytes);

                            await SaveProgressAsync(transfer.Id, item.Id, bytes, progressCancellation.Token);
                        }
                    }
                    catch (OperationCanceledException)when (progressCancellation.IsCancellationRequested)
                    {
                        // Expected when the upload finishes or is interrupted.
                    }
                }, CancellationToken.None);

                TransferFileAcknowledgment acknowledgment;

                try
                {
                    acknowledgment = await connection.SendTransferFileAsync(transfer.Id, item.Id, sourcePath, sourceHash, bytes => Interlocked.Exchange(ref uploadedBytes, bytes), cancellationToken);
                }
                finally
                {
                    await progressCancellation.CancelAsync();
                    await progressTask;
                }

                if (!string.Equals(sourceHash, acknowledgment.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("SHA-256 verification failed. The received file does not match the source.");
                }

                if (!acknowledgment.Completed)
                {
                    throw new InvalidOperationException("Receiver did not confirm transfer completion.");
                }

                item.Sha256 = sourceHash;
                
                database.ChangeTracker.Clear();

                TransferJob finalTransfer = await database.TransferJobs.Include(t => t.Items)
                    .FirstAsync(t => t.Id == transferId, cancellationToken);

                TransferItem finalItem = finalTransfer.Items.Single(i => i.Id == item.Id);

                finalItem.Sha256 = sourceHash;
                finalItem.TransferredBytes = finalItem.SizeBytes;

                finalTransfer.TransferredBytes = finalTransfer.TotalBytes;
                finalTransfer.Status = TransferStatus.Completed;
                finalTransfer.CompletedUtc = DateTime.UtcNow;
                finalTransfer.ErrorMessage = null;

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
        
        private async Task SaveProgressAsync(Guid transferId, Guid itemId, long bytes, CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

            RemoteLinkDbContext progressDatabase = scope.ServiceProvider.GetRequiredService<RemoteLinkDbContext>();

            await progressDatabase.TransferJobs.Where(t => t.Id == transferId && t.Status == TransferStatus.Transferring)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.TransferredBytes, bytes), cancellationToken);

            await progressDatabase.Set<TransferItem>().Where(i => i.Id == itemId && i.TransferId == transferId && progressDatabase.TransferJobs.Any(t => t.Id == i.TransferId && t.Status == TransferStatus.Transferring))
                .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.TransferredBytes, bytes), cancellationToken);
        }
    }
}