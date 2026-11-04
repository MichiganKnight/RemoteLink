import { useEffect, useState } from "react";

import IncomingPairingRequests from "../components/IncomingPairingRequests";

import type { DiscoveredDevice, PairingHandshakeVerification } from "../types/electron";

export default function DevicesPage() {
    const [devices, setDevices] = useState<DiscoveredDevice[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    const [pairingDeviceId, setPairingDeviceId] = useState<string | null>(null);

    const [pairingError, setPairingError] = useState<string | null>(null);

    const [verification, setVerification] = useState<PairingHandshakeVerification | null>(null);

    useEffect(() => {
        let active = true;
        let refreshing = false;

        async function refreshDevices() {
            if (refreshing) {
                return;
            }

            refreshing = true;

            try {
                const result = await window.remoteLink.getDevices();

                if (active) {
                    setDevices(result);
                    setError(null);
                }
            } catch (error) {
                if (active) {
                    setError(error instanceof Error ? error.message : "Unable to load devices");

                    setDevices([]);
                }
            } finally {
                refreshing = false;

                if (active) {
                    setLoading(false);
                }
            }
        }

        void refreshDevices();

        const interval = setInterval(() => {
            void refreshDevices();
        }, 5000);

        return () => {
            active = false;
            clearInterval(interval);
        }
    }, []);

    async function handlePair(device: DiscoveredDevice) {
        if (pairingDeviceId !== null || verification !== null) {
            return;
        }

        setPairingDeviceId(device.id);
        setPairingError(null);
        setVerification(null);

        try {
            const result = await window.remoteLink.startDiscoveredPairing(device.id);

            if (!result.signatureVerified) {
                throw new Error("The remote device's signature was not verified.");
            }

            setVerification(result);
        } catch (error) {
            setPairingError(error instanceof Error ? error.message : "Unable to initiate pairing.");
        } finally {
            setPairingDeviceId(null);
        }
    }

    return (
        <div>
            <IncomingPairingRequests/>

            <div className="d-flex justify-content-between align-items-center mb-4">
                <div>
                    <h5 className="mb-1">
                        Available Devices
                    </h5>

                    <p className="text-secondary mb-0">
                        Computers discovered on your local network.
                    </p>
                </div>

                <span className="badge text-bg-secondary">
                    {devices.length} Devices
                </span>
            </div>

            {error && (
                <div className="alert alert-danger" role="alert">
                    <i className="bi bi-exclamation-triangle me-2"/>
                    {error}
                </div>
            )}

            {pairingError && (
                <div className="alert alert-danger" role="alert">
                    <i className="bi bi-exclamation-triangle me-2"/>
                    {pairingError}
                </div>
            )}

            {verification && (
                <div className="content-card mb-4">
                    <div className="d-flex justify-content-between align-items-center mb-3">
                        <h5 className="mb-0">
                            Pairing Verification
                        </h5>

                        <span className="badge text-bg-warning">
                            Awaiting Mutual Verification
                        </span>
                    </div>

                    <p className="text-secondary">
                        The signed handshake with{" "}
                        <strong>{verification.remoteDeviceName}</strong>{" "}
                        was verified.
                    </p>

                    <div className="rounded-3 text-center p-4 mb-3" style={{ backgroundColor: "var(--bs-dark-bg-subtle, #151e2d)", border: "1px solid var(--bs-border-color)" }}>
                        <div className="small text-secondary mb-2">
                            <i className="bi bi-shield-lock me-2"/>
                            Six-Digit Verification Code
                        </div>

                        <div className="fw-bold font-monospace" style={{ fontSize: "clamp(1.75rem, 4vw, 2.5rem)", letterSpacing: "0.2rem", color: "var(--bs-body-color)" }}>
                            {verification.verificationCode.slice(0, 3)}
                            {" "}
                            {verification.verificationCode.slice(3)}
                        </div>

                        <div className="small text-secondary mt-3">
                            Compare this code on both computers.
                        </div>
                    </div>

                    <div className="small text-secondary mb-3">
                        Remote identity fingerprint:
                        <div className="font-monospace text-break mt-1">
                            {verification.remoteIdentityFingerprint}
                        </div>
                    </div>

                    <div className="alert alert-info mb-3">
                        The remote device has not yet completed mutual verification. Do not treat this device as trusted.
                    </div>

                    <button type="button" className="btn btn-outline-secondary" onClick={() => setVerification(null)}>
                        Dismiss
                    </button>
                </div>
            )}

            {loading ? (
                <div className="content-card">
                    Searching for devices...
                </div>
            ) : devices.length === 0 ? (
                <div className="content-card text-center py-5">
                    <i className="bi bi-pc-display fs-1 text-secondary"/>

                    <h5 className="mt-3">
                        No Devices Found
                    </h5>

                    <p className="text-secondary mb-0">
                        Run RemoteLink on another computer connected to the same local network.
                    </p>
                </div>
            ) : (
                <div className="row g-3">
                    {devices.map(device => (
                        <div key={device.id} className="col-md-6">
                            <div className="content-card">
                                <div className="d-flex align-items-center gap-3">
                                    <i className="bi bi-pc-display fs-2 text-primary"/>

                                    <div className="flex-grow-1">
                                        <h6 className="mb-1">
                                            {device.name}
                                        </h6>

                                        <div className="small text-secondary">
                                            {device.ipAddress}
                                        </div>
                                    </div>

                                    <span className="badge text-bg-success">
                                        Available
                                    </span>
                                </div>

                                <div className="small text-secondary mt-3">
                                    Last seen:{" "}
                                    {new Date(device.lastSeenUtc).toLocaleTimeString()}
                                </div>

                                <div className="mt-3">
                                    <button type="button" className="btn btn-primary btn-sm" disabled={!device.isOnline || pairingDeviceId !== null || verification !== null} onClick={() => {
                                        void handlePair(device)
                                    }}>
                                        {pairingDeviceId === device.id ? (
                                            <>
                                                <span className="spinner-border spinner-border-sm me-2" aria-hidden="true"/>
                                                Pairing...
                                            </>
                                        ) : (
                                            <>
                                                <i className="bi bi-link-45deg me-1"/>
                                                Pair Device
                                            </>
                                        )}
                                    </button>
                                </div>
                            </div>
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
}

