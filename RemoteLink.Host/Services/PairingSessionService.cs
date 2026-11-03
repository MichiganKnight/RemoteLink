using System.Collections.Concurrent;
using RemoteLink.Core.Models.Pairing;

namespace RemoteLink.Host.Services
{
    public sealed class PairingSessionService
    {
        private static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(2);
        
        private readonly ConcurrentDictionary<Guid, PairingRequest> _requests = new();
        
        private readonly object _sync = new();

        public PairingRequest CreateRequest(Guid requestId, Guid deviceId, string deviceName, string publicKey, string fingerprint)
        {
            if (requestId == Guid.Empty)
            {
                throw new ArgumentException("A valid request ID is required.", nameof(requestId));
            }
            
            if (deviceId == Guid.Empty)
            {
                throw new ArgumentException("A valid device ID is required.", nameof(deviceId));
            }

            if (string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(fingerprint))
            {
                throw new ArgumentException("Device identity information is required.");
            }
            
            DateTime now = DateTime.UtcNow;

            PairingRequest request = new()
            {
                RequestId = requestId,
                DeviceId = deviceId,
                DeviceName = deviceName,
                PublicKey = publicKey,
                Fingerprint = fingerprint,
                CreatedUtc = now,
                ExpiresUtc = now.Add(RequestLifetime),
                Status = PairingStatus.Pending
            };

            lock (_sync)
            {
                ExpireRequests();
                
                if (!_requests.TryAdd(request.RequestId, request))
                {
                    throw new InvalidOperationException("A pairing request with this ID already exists.");
                }
            }
            
            return Copy(request);
        }

        public IReadOnlyList<PairingRequest> GetRequests()
        {
            lock (_sync)
            {
                ExpireRequests();

                return _requests.Values.OrderByDescending(r => r.CreatedUtc).Select(Copy).ToList();
            }
        }

        public PairingRequest? GetRequest(Guid requestId)
        {
            lock (_sync)
            {
                ExpireRequests();
                
                return _requests.TryGetValue(requestId, out PairingRequest? request) ? Copy(request) : null;
            }
        }

        public bool TryUpdateStatus(Guid requestId, PairingStatus expectedStatus, PairingStatus newStatus)
        {
            lock (_sync)
            {
                ExpireRequests();

                if (!_requests.TryGetValue(requestId, out PairingRequest? request))
                {
                    return false;
                }

                if (request.Status != expectedStatus)
                {
                    return false;
                }

                if (request.ExpiresUtc <= DateTime.UtcNow)
                {
                    request.Status = PairingStatus.Expired;
                    
                    return false;
                }
                
                request.Status = newStatus;
                
                return true;
            }
        }

        public bool RejectRequest(Guid requestId)
        {
            return TryUpdateStatus(requestId, PairingStatus.Pending, PairingStatus.Rejected);
        }

        public int RemoveCompletedRequests()
        {
            lock (_sync)
            {
                ExpireRequests();
                
                Guid[] completed = _requests.Values
                    .Where(r => r.Status is PairingStatus.Paired or PairingStatus.Rejected or PairingStatus.Expired or PairingStatus.Failed)
                    .Select(r => r.RequestId)
                    .ToArray();

                int removed = 0;

                foreach (Guid requestId in completed)
                {
                    if (_requests.TryRemove(requestId, out _))
                    {
                        removed++;
                    }
                }
                
                return removed;
            }
        }

        private void ExpireRequests()
        {
            DateTime now = DateTime.UtcNow;

            foreach (PairingRequest request in _requests.Values)
            {
                if (request.ExpiresUtc <= now && request.Status is PairingStatus.Pending or PairingStatus.AwaitingConfirmation)
                {
                    request.Status = PairingStatus.Expired;
                }
            }
        }

        private static PairingRequest Copy(PairingRequest source)
        {
            return new PairingRequest
            {
                RequestId = source.RequestId,
                DeviceId = source.DeviceId,
                DeviceName = source.DeviceName,
                PublicKey = source.PublicKey,
                Fingerprint = source.Fingerprint,
                CreatedUtc = source.CreatedUtc,
                ExpiresUtc = source.ExpiresUtc,
                Status = source.Status
            };
        }
    }
}