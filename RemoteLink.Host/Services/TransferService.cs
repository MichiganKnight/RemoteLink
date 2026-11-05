using Microsoft.EntityFrameworkCore;
using RemoteLink.Core.Models.Transfers;
using RemoteLink.Core.Models.Transfers.Requests;
using RemoteLink.Core.Models.Transfers.Responses;
using RemoteLink.Host.Data;

namespace RemoteLink.Host.Services
{
    public sealed class TransferService(RemoteLinkDbContext database, DeviceIdentityService identity, TrustedDeviceService trustedDevices)
    {
        public async Task<TransferJobResponse> ReceiveOfferAsync(Guid authenticatedDeviceId, IncomingTransferRequest request, CancellationToken cancellationToken = default)
        {
            if (authenticatedDeviceId == Guid.Empty || trustedDevices.GetDevice(authenticatedDeviceId) is not { IsTrusted: true })
            {
                throw new UnauthorizedAccessException("Device is not trusted.");
            }

            if (request.TransferId == Guid.Empty || request.SenderDeviceId != authenticatedDeviceId || request.RecipientDeviceId != identity.DeviceId)
            {
                throw new ArgumentException("Invalid transfer identity.");
            }
            
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 255 || request.TotalBytes < 0 || request.Items.Count == 0 || request.Items.Count > 10000)
            {
                throw new ArgumentException("Invalid transfer metadata.");
            }

            long calculatedTotal = 0;
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

            foreach (IncomingTransferItem item in request.Items)
            {
                if (item.ItemId == Guid.Empty || item.SizeBytes < 0 || string.IsNullOrWhiteSpace(item.RelativePath) || item.RelativePath.Length > 2048)
                {
                    throw new ArgumentException("Invalid transfer item.");
                }

                string normalizedPath = item.RelativePath.Replace('\\', '/');
                
                if (normalizedPath.StartsWith('/') || normalizedPath.Contains(':') || normalizedPath.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part == "." || part == ".."))
                {
                    throw new ArgumentException("Unsafe relative transfer path.");
                }

                if (!paths.Add(normalizedPath))
                {
                    throw new ArgumentException("Duplicate transfer path.");
                }
                
                if (!item.IsDirectory)
                {
                    calculatedTotal = checked(calculatedTotal + item.SizeBytes);
                }
                else if (item.SizeBytes != 0)
                {
                    throw new ArgumentException("Directories cannot contain file bytes.");
                }
            }
            
            if (calculatedTotal != request.TotalBytes)
            {
                throw new ArgumentException("Transfer size mismatch.");
            }

            TransferJob? existing = await database.TransferJobs.Include(t => t.Items)
                .FirstOrDefaultAsync(t => t.Id == request.TransferId, cancellationToken);

            if (existing is not null)
            {
                if (existing.Direction != TransferDirection.Incoming || existing.RemoteDeviceId != authenticatedDeviceId)
                {
                    throw new InvalidOperationException("Transfer ID conflict.");
                }

                return ToResponse(existing);
            }

            TransferJob transfer = new()
            {
                Id = request.TransferId,
                RemoteDeviceId = authenticatedDeviceId,
                Name = request.Name,
                Direction = TransferDirection.Incoming,
                Status = TransferStatus.AwaitingApproval,
                TotalBytes = request.TotalBytes,
                CreatedUtc = DateTime.UtcNow,
                Items = request.Items.Select(item => new TransferItem
                {
                    Id = item.ItemId,
                    RelativePath = item.RelativePath.Replace('\\', '/'),
                    SizeBytes = item.SizeBytes,
                    IsDirectory = item.IsDirectory
                }).ToList()
            };
            
            database.TransferJobs.Add(transfer);

            await database.SaveChangesAsync(cancellationToken);

            return ToResponse(transfer);
        }

        public async Task<TransferJobResponse> DecideAsync(Guid transferId, bool accepted, CancellationToken cancellationToken = default)
        {
            TransferJob transfer = await database.TransferJobs.Include(t => t.Items)
                                       .FirstOrDefaultAsync(t => t.Id == transferId, cancellationToken) ?? throw new KeyNotFoundException("Transfer not found.");

            if (transfer.Direction != TransferDirection.Incoming || transfer.Status != TransferStatus.AwaitingApproval)
            {
                throw new InvalidOperationException("Transfer is not awaiting approval.");
            }

            transfer.Status = accepted ? TransferStatus.Accepted : TransferStatus.Rejected;

            await database.SaveChangesAsync(cancellationToken);

            return ToResponse(transfer);
        }
        
        public async Task<IReadOnlyList<TransferJobResponse>> GetTransfersAsync(CancellationToken cancellationToken = default)
        {
            List<TransferJob> transfers = await database.TransferJobs.AsNoTracking()
                .Include(t => t.Items)
                .OrderByDescending(t => t.CreatedUtc)
                .Take(100)
                .ToListAsync(cancellationToken);

            return transfers.Select(ToResponse).ToList();
        }
        
        public async Task<TransferJobResponse> ApplyRemoteDecisionAsync(Guid authenticatedDeviceId, TransferDecisionNotification notification, CancellationToken cancellationToken = default)
        {
            if (authenticatedDeviceId == Guid.Empty ||
                trustedDevices.GetDevice(authenticatedDeviceId) is not { IsTrusted: true })
            {
                throw new UnauthorizedAccessException("Device is not trusted.");
            }

            if (notification.TransferId == Guid.Empty || notification.SenderDeviceId != identity.DeviceId || notification.RecipientDeviceId != authenticatedDeviceId)
            {
                throw new ArgumentException("Invalid transfer decision identity.");
            }

            TransferJob transfer = await database.TransferJobs.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == notification.TransferId, cancellationToken) ?? throw new KeyNotFoundException("Transfer not found.");

            if (transfer.Direction != TransferDirection.Outgoing || transfer.RemoteDeviceId != authenticatedDeviceId)
            {
                throw new InvalidOperationException("Transfer does not belong to the authenticated device.");
            }

            TransferStatus desiredStatus = notification.Accepted ? TransferStatus.Accepted : TransferStatus.Rejected;

            if (transfer.Status == desiredStatus)
            {
                return ToResponse(transfer);
            }

            if (transfer.Status != TransferStatus.AwaitingApproval)
            {
                throw new InvalidOperationException("Transfer is not awaiting approval.");
            }

            transfer.Status = desiredStatus;

            await database.SaveChangesAsync(cancellationToken);

            return ToResponse(transfer);
        }

        private static TransferJobResponse ToResponse(TransferJob transfer)
        {
            return new TransferJobResponse
            {
                Id = transfer.Id,
                RemoteDeviceId = transfer.RemoteDeviceId,
                Name = transfer.Name,
                Direction = transfer.Direction,
                Status = transfer.Status,
                TotalBytes = transfer.TotalBytes,
                TransferredBytes = transfer.TransferredBytes,
                CreatedUtc = AsUtc(transfer.CreatedUtc),
                StartedUtc = AsUtc(transfer.StartedUtc),
                CompletedUtc = AsUtc(transfer.CompletedUtc),
                ErrorMessage = transfer.ErrorMessage,
                Items = transfer.Items.Select(item => new TransferItemResponse
                {
                    Id = item.Id,
                    RelativePath = item.RelativePath,
                    SizeBytes = item.SizeBytes,
                    TransferredBytes = item.TransferredBytes,
                    IsDirectory = item.IsDirectory
                }).ToList()
            };
        }

        private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

        private static DateTime? AsUtc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
    }
}