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
    public sealed class LanTransferController(PeerAuthenticationService authentication, TransferService transfers, TransferStorageService storage, RemoteLinkDbContext database) : ControllerBase
    {
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

            if (transfer.Direction != TransferDirection.Incoming || transfer.RemoteDeviceId != authenticatedDeviceId || transfer.Status is not (TransferStatus.Accepted or TransferStatus.Transferring))
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

            string temporaryPath = storage.GetTemporaryFilePath(transferId, itemId);

            await using (FileStream output = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
            {
                
                byte[] buffer = new byte[1024 * 1024];
                long remaining = item.SizeBytes;

                while (remaining > 0)
                {
                    int bytesToRead = (int)Math.Min(buffer.Length, remaining);

                    int bytesRead = await Request.Body.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);

                    remaining -= bytesRead;
                }

                if (remaining != 0)
                {
                    return BadRequest("Received file size mismatch.");
                }

                if (await Request.Body.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
                {
                    return BadRequest("Received more bytes than expected.");
                }
            }

            FileInfo receivedFile = new(temporaryPath);

            if (receivedFile.Length != item.SizeBytes)
            {
                return BadRequest("Received file size mismatch.");
            }

            string hash = await TransferStorageService.CalculateSha256Async(temporaryPath, cancellationToken);

            string destination = storage.GetCompletedFilePath(item.RelativePath);

            System.IO.File.Move(temporaryPath, destination);

            item.TransferredBytes = item.SizeBytes;
            item.Sha256 = hash;

            transfer.TransferredBytes = transfer.Items.Where(i => !i.IsDirectory)
                .Sum(i => i.TransferredBytes);

            transfer.Status = transfer.TransferredBytes == transfer.TotalBytes ? TransferStatus.Completed : TransferStatus.Transferring;

            transfer.StartedUtc ??= DateTime.UtcNow;

            if (transfer.Status == TransferStatus.Completed)
            {
                transfer.CompletedUtc = DateTime.UtcNow;
            }

            await database.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                transferId,
                itemId,
                receivedBytes = item.SizeBytes,
                sha256 = hash,
                completed = transfer.Status == TransferStatus.Completed
            });
        }
    }
}