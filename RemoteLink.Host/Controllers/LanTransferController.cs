using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RemoteLink.Core.Models.Transfers;
using RemoteLink.Core.Models.Transfers.Requests;
using RemoteLink.Host.Data;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/lan/transfers")]
    public sealed class LanTransferController(PeerAuthenticationService authentication, TransferService transfers, TransferStorageService storage, RemoteLinkDbContext database, IServiceScopeFactory scopeFactory) : ControllerBase
    {
        private static readonly ConcurrentDictionary<Guid, byte> ActiveUploads = new();

        [HttpPost("offers")]
        public async Task<IActionResult> ReceiveOffer([FromBody] IncomingTransferRequest request, CancellationToken cancellationToken)
        {
            string? authorization = Request.Headers.Authorization.ToString();

            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized();
            }

            string token = authorization["Bearer ".Length..].Trim();

            if (!authentication.TryValidateSession(token, out Guid authenticatedDeviceId))
            {
                return Unauthorized();
            }

            try
            {
                return Ok(await transfers.ReceiveOfferAsync(authenticatedDeviceId, request, cancellationToken));
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (ArgumentException exception)
            {
                return BadRequest(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(exception.Message);
            }
            catch (OverflowException)
            {
                return BadRequest("Transfer size exceeds supported limits.");
            }
        }

        [HttpPost("{transferId:guid}/decision")]
        public async Task<IActionResult> ReceiveDecision(Guid transferId, [FromBody] TransferDecisionNotification notification, CancellationToken cancellationToken)
        {
            string? authorization = Request.Headers.Authorization.ToString();

            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized();
            }

            string token = authorization["Bearer ".Length..].Trim();

            if (!authentication.TryValidateSession(token, out Guid authenticatedDeviceId))
            {
                return Unauthorized();
            }

            if (notification.TransferId != transferId)
            {
                return BadRequest("Transfer ID mismatch.");
            }

            try
            {
                return Ok(await transfers.ApplyRemoteDecisionAsync(authenticatedDeviceId, notification, cancellationToken));
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (ArgumentException exception)
            {
                return BadRequest(exception.Message);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(exception.Message);
            }
        }

        [HttpPut("{transferId:guid}/items/{itemId:guid}/content")]
        [RequestSizeLimit(long.MaxValue)]
        public async Task<IActionResult> ReceiveFileContent(Guid transferId, Guid itemId, CancellationToken cancellationToken)
        {
            string authorization = Request.Headers.Authorization.ToString();

            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized();
            }

            string token = authorization["Bearer ".Length..].Trim();

            if (!authentication.TryValidateSession(token, out Guid authenticatedDeviceId))
            {
                return Unauthorized();
            }

            TransferJob? transfer = await database.TransferJobs.Include(t => t.Items)
                .FirstOrDefaultAsync(t => t.Id == transferId, cancellationToken);

            if (transfer is null)
            {
                return NotFound();
            }

            if (transfer.Direction != TransferDirection.Incoming || transfer.RemoteDeviceId != authenticatedDeviceId || transfer.Status != TransferStatus.Accepted)
            {
                return StatusCode(403);
            }

            TransferItem? item = transfer.Items.FirstOrDefault(i => i.Id == itemId && !i.IsDirectory);

            if (item is null)
            {
                return NotFound();
            }

            if (Request.ContentLength != item.SizeBytes)
            {
                return BadRequest("File size does not match transfer metadata.");
            }

            string expectedHash = Request.Headers["X-File-Sha256"].ToString();

            if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
            {
                return BadRequest("Missing or invalid SHA-256 header.");
            }

            if (!ActiveUploads.TryAdd(transferId, 0))
            {
                return Conflict("This transfer is already being received.");
            }

            string? temporaryPath = null;
            bool completed = false;
            bool started = false;

            try
            {
                if (transfer.Items.Count != 1)
                {
                    return BadRequest("Only single-file transfers are currently supported.");
                }

                temporaryPath = storage.GetTemporaryFilePath(transferId, itemId);

                transfer.Status = TransferStatus.Transferring;
                transfer.StartedUtc ??= DateTime.UtcNow;
                transfer.ErrorMessage = null;

                await database.SaveChangesAsync(cancellationToken);
                started = true;

                byte[] buffer = new byte[1024 * 1024];
                long receivedBytes = 0;
                long lastSavedBytes = 0;

                Stopwatch progressClock = Stopwatch.StartNew();
                TimeSpan progressInterval = TimeSpan.FromMilliseconds(500);

                await using (FileStream output = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
                {
                    long remaining = item.SizeBytes;

                    while (remaining > 0)
                    {
                        int requested = (int)Math.Min(buffer.Length, remaining);

                        int read = await Request.Body.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);

                        if (read == 0)
                        {
                            throw new EndOfStreamException("The sender disconnected before the file was fully received.");
                        }

                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                        receivedBytes += read;
                        remaining -= read;

                        if (progressClock.Elapsed >= progressInterval)
                        {
                            await SaveIncomingProgressAsync(transferId, itemId, receivedBytes, cancellationToken);

                            lastSavedBytes = receivedBytes;
                            progressClock.Restart();
                        }
                    }

                    if (await Request.Body.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
                    {
                        throw new InvalidDataException("Received more bytes than the approved file size.");
                    }

                    await output.FlushAsync(cancellationToken);
                }

                if (lastSavedBytes != receivedBytes)
                {
                    await SaveIncomingProgressAsync(transferId, itemId, receivedBytes, cancellationToken);
                }

                string actualHash = await TransferStorageService.CalculateSha256Async(temporaryPath, cancellationToken);

                byte[] expectedHashBytes = Convert.FromHexString(expectedHash);
                byte[] actualHashBytes = Convert.FromHexString(actualHash);

                if (!CryptographicOperations.FixedTimeEquals(expectedHashBytes, actualHashBytes))
                {
                    throw new InvalidDataException("SHA-256 verification failed. Received file was not saved.");
                }

                string destination = storage.GetCompletedFilePath(item.RelativePath);

                System.IO.File.Move(temporaryPath, destination);
                temporaryPath = null;

                database.ChangeTracker.Clear();

                TransferJob finalTransfer = await database.TransferJobs.Include(t => t.Items)
                    .FirstAsync(t => t.Id == transferId, cancellationToken);

                TransferItem finalItem = finalTransfer.Items.Single(i => i.Id == itemId);

                finalItem.TransferredBytes = item.SizeBytes;
                finalItem.Sha256 = actualHash;

                finalTransfer.TransferredBytes = item.SizeBytes;
                finalTransfer.Status = TransferStatus.Completed;
                finalTransfer.CompletedUtc = DateTime.UtcNow;
                finalTransfer.ErrorMessage = null;

                await database.SaveChangesAsync(cancellationToken);

                completed = true;

                return Ok(new TransferFileAcknowledgment(transferId, itemId, item.SizeBytes, actualHash, true));
            }
            catch (OperationCanceledException)
            {
                if (started && !completed)
                {
                    await MarkIncomingFailedAsync(transferId, "File transfer was interrupted.");
                }

                throw;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException)
            {
                if (started && !completed)
                {
                    await MarkIncomingFailedAsync(transferId, exception.Message);
                }

                return StatusCode(StatusCodes.Status422UnprocessableEntity, exception.Message);
            }
            finally
            {
                ActiveUploads.TryRemove(transferId, out _);

                if (temporaryPath is not null)
                {
                    try
                    {
                        if (System.IO.File.Exists(temporaryPath))
                        {
                            System.IO.File.Delete(temporaryPath);
                        }
                    }
                    catch (IOException)
                    {
                        // Cleanup can be retried during maintenance.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Cleanup can be retried during maintenance.
                    }
                }
            }
        }

        private async Task SaveIncomingProgressAsync(Guid transferId, Guid itemId, long bytes, CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

            RemoteLinkDbContext progressDatabase = scope.ServiceProvider.GetRequiredService<RemoteLinkDbContext>();

            int updated = await progressDatabase.TransferJobs.Where(t => t.Id == transferId && t.Status == TransferStatus.Transferring)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.TransferredBytes, bytes), cancellationToken);

            if (updated == 0)
            {
                throw new InvalidOperationException("Incoming transfer is no longer active.");
            }

            await progressDatabase.Set<TransferItem>().Where(i => i.Id == itemId && i.TransferId == transferId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.TransferredBytes, bytes), cancellationToken);
        }

        private async Task MarkIncomingFailedAsync(Guid transferId, string message)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

                RemoteLinkDbContext failureDatabase = scope.ServiceProvider.GetRequiredService<RemoteLinkDbContext>();

                string error = message.Length > 2048 ? message[..2048] : message;

                await failureDatabase.TransferJobs.Where(t => t.Id == transferId && t.Status == TransferStatus.Transferring)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.Status, TransferStatus.Failed).SetProperty(t => t.ErrorMessage, error));
            }
            catch (Exception)
            {
                // Preserve the original transfer error.
                // Production logging can be added here.
            }
        }
    }
}