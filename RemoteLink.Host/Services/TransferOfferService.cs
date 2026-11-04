using RemoteLink.Core.Models.Transfers;
using RemoteLink.Core.Models.Transfers.Requests;
using RemoteLink.Core.Models.Transfers.Responses;
using RemoteLink.Host.Data;

namespace RemoteLink.Host.Services
{
    public sealed class TransferOfferService(RemoteLinkDbContext database, DeviceIdentityService identity, TrustedDeviceService trustedDevices, ChatPeerAddressResolver addresses, TrustedPeerClientService peerClient)
    {
        public async Task<TransferJobResponse> CreateAndSendOfferAsync(CreateTransferOfferRequest request, CancellationToken cancellationToken = default)
        {
            if (request.RecipientDeviceId == Guid.Empty || request.RecipientDeviceId == identity.DeviceId)
            {
                throw new ArgumentException("Invalid recipient device.");
            }

            if (trustedDevices.GetDevice(request.RecipientDeviceId) is not { IsTrusted: true })
            {
                throw new UnauthorizedAccessException("Recipient is not a trusted device.");
            }

            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 255 || request.Items is null || request.Items.Count == 0 || request.Items.Count > 10000)
            {
                throw new ArgumentException("Invalid transfer metadata.");
            }

            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

            long totalBytes = 0;

            foreach (CreateTransferItemRequest item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item.RelativePath) || item.RelativePath.Length > 2048 || item.SizeBytes < 0)
                {
                    throw new ArgumentException("Invalid transfer item.");
                }

                string path = item.RelativePath.Replace('\\', '/');

                if (path.StartsWith('/') || path.Contains(':') || path.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."))
                {
                    throw new ArgumentException("Unsafe transfer path.");
                }

                if (!paths.Add(path))
                {
                    throw new ArgumentException("Duplicate transfer path.");
                }

                if (item.IsDirectory)
                {
                    if (item.SizeBytes != 0)
                    {
                        throw new ArgumentException("Directory entries must have zero size.");
                    }
                }
                else
                {
                    totalBytes = checked(totalBytes + item.SizeBytes);
                }
            }

            string address = addresses.Resolve(request.RecipientDeviceId) ?? throw new InvalidOperationException("Recipient device is not currently discovered on LAN.");

            Guid transferId = Guid.NewGuid();

            List<TransferItem> items = request.Items.Select(item => new TransferItem
            {
                Id = Guid.NewGuid(),
                RelativePath = item.RelativePath.Replace('\\', '/'),
                SizeBytes = item.SizeBytes,
                IsDirectory = item.IsDirectory
            }).ToList();

            TransferJob transfer = new()
            {
                Id = transferId,
                RemoteDeviceId = request.RecipientDeviceId,
                Name = request.Name,
                Direction = TransferDirection.Outgoing,
                Status = TransferStatus.Pending,
                TotalBytes = totalBytes,
                CreatedUtc = DateTime.UtcNow,
                Items = items
            };

            database.TransferJobs.Add(transfer);

            await database.SaveChangesAsync(cancellationToken);

            IncomingTransferRequest offer = new()
            {
                TransferId = transfer.Id,
                SenderDeviceId = identity.DeviceId,
                RecipientDeviceId = request.RecipientDeviceId,
                Name = transfer.Name,
                TotalBytes = transfer.TotalBytes,
                Items = transfer.Items.Select(item => new IncomingTransferItem
                {
                    ItemId = item.Id,
                    RelativePath = item.RelativePath,
                    SizeBytes = item.SizeBytes,
                    IsDirectory = item.IsDirectory
                }).ToList()
            };

            try
            {
                using AuthenticatedPeerConnection connection = await peerClient.ConnectAsync(request.RecipientDeviceId, address, cancellationToken);

                TransferJobResponse remote = await connection.SendTransferOfferAsync(offer, cancellationToken);

                if (remote.Id != transfer.Id || remote.Direction != TransferDirection.Incoming || remote.Status != TransferStatus.AwaitingApproval)
                {
                    throw new InvalidOperationException("Remote device returned unexpected transfer metadata.");
                }

                transfer.Status = TransferStatus.AwaitingApproval;

                await database.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)when (exception is not OperationCanceledException)
            {
                transfer.Status = TransferStatus.Failed;
                transfer.ErrorMessage = exception.Message.Length > 2048 ? exception.Message[..2048] : exception.Message;

                await database.SaveChangesAsync(cancellationToken);

                throw;
            }

            return new TransferJobResponse
            {
                Id = transfer.Id,
                RemoteDeviceId = transfer.RemoteDeviceId,
                Name = transfer.Name,
                Direction = transfer.Direction,
                Status = transfer.Status,
                TotalBytes = transfer.TotalBytes,
                TransferredBytes = transfer.TransferredBytes,
                CreatedUtc = DateTime.SpecifyKind(transfer.CreatedUtc, DateTimeKind.Utc),
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
    }
}