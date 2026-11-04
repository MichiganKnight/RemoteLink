import { useEffect, useState } from "react";
import type { IncomingPairingRequest, PairingLocalVerification } from "../types/electron";

const PAIRING_STATUS_PENDING = 0;
const PAIRING_STATUS_AWAITING_CONFIRMATION = 1;

export default function IncomingPairingRequests() {
    const [requests, setRequests] = useState<IncomingPairingRequest[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    const [verifications, setVerifications] = useState<Record<string, PairingLocalVerification>>({});

    useEffect(() => {
        let active = true;
        let refreshing = false;

        async function refreshRequests() {
            if (refreshing) {
                return;
            }

            refreshing = true;

            try {
                const result = await window.remoteLink.getIncomingPairingRequests();

                if (active) {
                    setRequests(result);

                    const pending = result.filter(request => (request.status === PAIRING_STATUS_PENDING || request.status === PAIRING_STATUS_AWAITING_CONFIRMATION) && new Date(request.expiresUtc).getTime() > Date.now());

                    const results = await Promise.allSettled(pending.map(request => window.remoteLink.getLocalPairingVerification(request.requestId)));

                    if (active) {
                        const next: Record<string, PairingLocalVerification> = {};

                        results.forEach((result, index) => {
                            if (result.status === "fulfilled" && result.value.requestId === pending[index].requestId && new Date(result.value.expiresUtc).getTime() >  Date.now()) {
                                next[pending[index].requestId] = result.value;
                            }
                        });

                        setVerifications(next);
                    }

                    setError(null);
                }
            } catch (exception) {
                if (active) {
                    setError(exception instanceof Error ? exception.message : "Unable to load pairing requests.");
                }
            } finally {
                refreshing = false;

                if (active) {
                    setLoading(false);
                }
            }
        }

        void refreshRequests();

        const interval = window.setInterval(() => {
            void refreshRequests();
        }, 3000);

        return () => {
            active = false;

            window.clearInterval(interval);
        };
    }, []);

    const pendingRequests = requests.filter(request => (request.status === PAIRING_STATUS_PENDING || request.status === PAIRING_STATUS_AWAITING_CONFIRMATION) && new Date(request.expiresUtc).getTime() > Date.now());

    return (
        <section className="mb-5">
            <div className="d-flex justify-content-between align-items-center mb-4">
                <div>
                    <h5 className="mb-1">
                        Incoming Pairing Requests
                    </h5>

                    <p className="text-secondary mb-0">
                        Computers requesting permission to connect to this device.
                    </p>
                </div>

                <span className="badge text-bg-secondary">
                    {pendingRequests.length} Pending
                </span>
            </div>

            {error && (
                <div className="alert alert-danger" role="alert">
                    <i className="bi bi-exclamation-triangle me-2"/>
                    {error}
                </div>
            )}

            {loading ? (
                <div className="content-card">
                    <span className="text-secondary">
                        Checking for pairing requests...
                    </span>
                </div>
            ) : pendingRequests.length === 0 ? (
                <div className="content-card pairing-empty-state">
                    <i className="bi bi-shield-lock pairing-empty-icon"/>

                    <div>
                        <h6 className="mb-1">
                            No Incoming Pairing Requests
                        </h6>

                        <p className="text-secondary mb-0">
                            Requests from other computers will appear here.
                        </p>
                    </div>
                </div>
            ) : (
                <div className="row g-3">
                    {pendingRequests.map(request => (
                        <div key={request.requestId} className="col-12">
                            <div className="content-card pairing-request-card">
                                <div className="d-flex align-items-start gap-3">
                                    <div className="pairing-device-icon">
                                        <i className="bi bi-pc-display"/>
                                    </div>

                                    <div className="flex-grow-1 pairing-request-details">
                                        <div className="d-flex align-items-center gap-2 flex-wrap">
                                            <h6 className="mb-0">
                                                {request.deviceName}
                                            </h6>

                                            <span className={`badge ${request.status === PAIRING_STATUS_AWAITING_CONFIRMATION ? "text-bg-info" : "text-bg-warning"}`}>
                                                {request.status === PAIRING_STATUS_AWAITING_CONFIRMATION ? "Awaiting Confirmation" : "Pending Authentication"}
                                            </span>
                                        </div>

                                        <div className="small text-secondary mt-2">
                                            Device ID: {request.deviceId}
                                        </div>

                                        <div className="pairing-fingerprint mt-3">
                                            <div className="small text-secondary mb-1">
                                                Identity Fingerprint
                                            </div>

                                            <code>
                                                {request.fingerprint}
                                            </code>
                                        </div>

                                        <div className="small text-secondary mt-3">
                                            <i className="bi bi-clock me-2"/>
                                            Expires at{" "}
                                            {new Date(request.expiresUtc).toLocaleTimeString()}
                                        </div>

                                        <div className="d-flex gap-2 mt-3">
                                            <button type="button" className="btn btn-primary btn-sm" disabled>
                                                <i className="bi bi-check-lg me-2"/>
                                                Approve
                                            </button>

                                            <button type="button" className="btn btn-outline-secondary btn-sm" disabled>
                                                <i className="bi bi-x-lg me-2"/>
                                                Reject
                                            </button>
                                        </div>

                                        {verifications[request.requestId] && (
                                            <div className="rounded p-3 mt-3 text-center" style={{ backgroundColor: "#151e2d", border: "1px solid #334155", color: "#f1f5f9" }}>
                                                <div className="small mb-2" style={{ color: "#94a3b8" }}>
                                                    <i className="bi bi-shield-lock me-2" />
                                                    Provisional Verification Code
                                                </div>

                                                <div className="fw-bold font-monospace" style={{ fontSize: "2rem", letterSpacing: "0.2rem" }}>
                                                    {verifications[request.requestId].verificationCode.slice(0, 3)}
                                                    {" "}
                                                    {verifications[request.requestId].verificationCode.slice(3)}
                                                </div>

                                                <div className="small text-break mt-2" style={{ color: "#94a3b8" }}>
                                                    Request: {request.requestId}
                                                </div>

                                                <div className="small mt-2" style={{ color: "#94a3b8" }}>
                                                    Not authorized until mutual verification completes.
                                                </div>
                                            </div>
                                        )}

                                        <div className="small text-secondary mt-3">
                                            Approval will be enabled after secure
                                            identity verification is implemented.
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>
                    ))}
                </div>
            )}
        </section>
    );
}