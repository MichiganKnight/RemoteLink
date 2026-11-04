namespace RemoteLink.Host.Services
{
    public enum PairingDecisionState
    {
        Pending,
        Approved,
        Rejected
    }

    public sealed class PairingApprovalService
    {
        private readonly Dictionary<Guid, ApprovalState> _approvals = new();
        private readonly object _sync = new();

        public bool TryCreate(Guid requestId, Guid remoteDeviceId, DateTime expiresUtc)
        {
            if (requestId == Guid.Empty || remoteDeviceId == Guid.Empty || expiresUtc <= DateTime.UtcNow)
            {
                return false;
            }

            lock (_sync)
            {
                CleanupExpiredCore();

                if (_approvals.ContainsKey(requestId))
                {
                    return false;
                }

                _approvals.Add(requestId, new ApprovalState
                {
                    RequestId = requestId,
                    RemoteDeviceId = remoteDeviceId,
                    ExpiresUtc = expiresUtc
                });

                return true;
            }
        }

        public PairingApprovalSnapshot? GetState(Guid requestId)
        {
            lock (_sync)
            {
                CleanupExpiredCore();

                return _approvals.TryGetValue(requestId, out ApprovalState? state) ? Snapshot(state) : null;
            }
        }

        public PairingApprovalSnapshot? SetLocalApproval(Guid requestId, bool approved)
        {
            lock (_sync)
            {
                CleanupExpiredCore();

                if (!_approvals.TryGetValue(requestId, out ApprovalState? state))
                {
                    return null;
                }

                PairingDecisionState incomingDecision = approved ? PairingDecisionState.Approved : PairingDecisionState.Rejected;
                
                if (state.LocalDecision == incomingDecision)
                {
                    return Snapshot(state);
                }
                
                if (state.LocalDecision != PairingDecisionState.Pending || state.RemoteDecision == PairingDecisionState.Rejected)
                {
                    return null;
                }

                state.LocalDecision = incomingDecision;

                return Snapshot(state);
            }
        }

        public PairingApprovalSnapshot? SetRemoteApproval(Guid requestId, Guid remoteDeviceId, bool approved)
        {
            lock (_sync)
            {
                CleanupExpiredCore();

                if (!_approvals.TryGetValue(requestId, out ApprovalState? state) ||
                    state.RemoteDeviceId != remoteDeviceId)
                {
                    return null;
                }

                PairingDecisionState incomingDecision = approved ? PairingDecisionState.Approved : PairingDecisionState.Rejected;
                
                if (state.RemoteDecision == incomingDecision)
                {
                    return Snapshot(state);
                }
                
                if (state.RemoteDecision != PairingDecisionState.Pending || state.LocalDecision == PairingDecisionState.Rejected)
                {
                    return null;
                }

                state.RemoteDecision = incomingDecision;

                return Snapshot(state);
            }
        }

        public bool Remove(Guid requestId)
        {
            lock (_sync)
            {
                return _approvals.Remove(requestId);
            }
        }

        private void CleanupExpiredCore()
        {
            DateTime now = DateTime.UtcNow;

            Guid[] expired = _approvals.Where(entry => entry.Value.ExpiresUtc <= now)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (Guid requestId in expired)
            {
                _approvals.Remove(requestId);
            }
        }

        private static PairingApprovalSnapshot Snapshot(ApprovalState state)
        {
            return new PairingApprovalSnapshot
            {
                RequestId = state.RequestId,
                RemoteDeviceId = state.RemoteDeviceId,
                LocalDecision = state.LocalDecision,
                RemoteDecision = state.RemoteDecision,
                ExpiresUtc = state.ExpiresUtc
            };
        }

        private sealed class ApprovalState
        {
            public required Guid RequestId { get; init; }
            public required Guid RemoteDeviceId { get; init; }
            public required DateTime ExpiresUtc { get; init; }

            public PairingDecisionState LocalDecision { get; set; }
            public PairingDecisionState RemoteDecision { get; set; }
        }
    }

    public sealed class PairingApprovalSnapshot
    {
        public required Guid RequestId { get; init; }
        public required Guid RemoteDeviceId { get; init; }

        public required PairingDecisionState LocalDecision { get; init; }
        public required PairingDecisionState RemoteDecision { get; init; }

        public required DateTime ExpiresUtc { get; init; }

        public bool LocalApproved => LocalDecision == PairingDecisionState.Approved;

        public bool RemoteApproved => RemoteDecision == PairingDecisionState.Approved;

        public bool Rejected => LocalDecision == PairingDecisionState.Rejected || RemoteDecision == PairingDecisionState.Rejected;

        public bool BothApproved => LocalApproved && RemoteApproved && !Rejected;
    }
}