import { useEffect, useState } from "react";

interface IncomingPairingRequests {
    requestId: string;
    deviceId: string;
    deviceName: string;
    publicKey: string;
    fingerprint: string;
    createdUtc: string;
    expiresUtc: string;
    status: number;
}

const PAIRING_STATUS_PENDING = 0;

export default function IncomingPairingRequests() {
    const [requests, setRequests] = useState<IncomingPairingRequests[]>([]);

    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let active = true;

        async function loadRequests() {
            try {
                const result = await window.remoteLink.getIncomingPairingRequests();

                if (active) {
                    setRequests(result);
                    setError(null);
                }
            } catch (exception) {
                if (active) {
                    setError(exception instanceof Error ? exception.message : "Failed to load pairing requests.");
                }
            }
        }

        void loadRequests();

        const interval = window.setInterval(() => void loadRequests(), 3000);

        return () => {
            active = false;
            window.clearInterval(interval);
        };
    }, []);

    const pendingRequests = requests.filter(request => request.status === PAIRING_STATUS_PENDING);

    return (
        <div className="card mb-4">
            <div className="card-header d-flex justify-content-between align-items-center">
                <span>
                    <i className="bi bi-shield-lock me-2" />
                    Incoming Pairing Requests
                </span>

                <span className="badge text-bg-secondary">
                    {pendingRequests.length}
                </span>
            </div>

            <div className="card-body">
                {error && (
                    <div className="alert alert-danger" role="alert">
                        {error}
                    </div>
                )}

                {pendingRequests.length === 0 ? (
                    <p className="text-body-secondary mb-0">
                        No incoming pairing requests
                    </p>
                ) : (
                    <div className="d-flex flex-column gap-3">
                        {pendingRequests.map(request => (
                            <div key={request.requestId} className="border rounded p-3">
                                <div className="d-flex align-items-center gap-2 mb-2">
                                    <i className="bi bi-pc-display" />

                                    <strong>
                                        {request.deviceName}
                                    </strong>

                                    <span className="badge text-bg-warning">
                                        Pending
                                    </span>
                                </div>

                                <div className="small text-body-secondary">
                                    <div>
                                        Device ID:
                                        {" "}
                                        {request.requestId}
                                    </div>

                                    <div className="mt-2">
                                        Identity fingerprint:
                                    </div>

                                    <code className="d-block text-break">
                                        {request.fingerprint}
                                    </code>

                                    <div className="mt-2">
                                        Expires:
                                        {" "}
                                        {new Date(request.expiresUtc).toLocaleTimeString()}
                                    </div>
                                </div>

                                <div className="d-flex gap-2 mt-3">
                                    <button type="button" className="btn btn-primary btn-sm" disabled>
                                        Approve
                                    </button>

                                    <button type="button" className="btn btn-outline-secondary btn-sm" disabled>
                                        Reject
                                    </button>
                                </div>

                                <div className="small text-body-secondary mt-2">
                                    Approval will be available after identity verification is implemented.
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </div>
    );
}