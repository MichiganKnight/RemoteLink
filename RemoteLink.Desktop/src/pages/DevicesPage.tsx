import { useEffect, useState } from "react";

import IncomingPairingRequests from "../components/IncomingPairingRequests";

import type { DiscoveredDevice, PairingHandshakeVerification, PairingApprovalState, TrustedDevice } from "../types/electron";

export default function DevicesPage() {
    const [devices, setDevices] = useState<DiscoveredDevice[]>([]);
    const [loading, setLoading] = useState(true);
    const [trusted, setTrusted] = useState<TrustedDevice[]>([]);
    const [decision, setDecision] = useState<PairingApprovalState | null>(null);
    const [decisionBusy, setDecisionBusy] = useState(false);
    const [pairedMessage, setPairedMessage] = useState<string | null>(null);

    async function chooseDecision(approved: boolean) {
        if (!verification) {
            return;
        }

        setDecisionBusy(true); setPairingError(null);

        try {
            const result = await window.remoteLink.decideOutgoingPairing(verification.requestId, approved);

            setDecision(result);

            if (!approved) {
                setVerification(null);
            }
        } catch (e) {
            setPairingError(e instanceof Error ? e.message : "Decision delivery failed. Try again.");
        } finally {
            setDecisionBusy(false);
        }
    }

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
                const paired = await window.remoteLink.getTrustedDevices();

                if (active) {
                    setDevices(result);
                    setTrusted(paired);
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
            setDecision(null); setPairedMessage(null);
        } catch (error) {
            setPairingError(error instanceof Error ? error.message : "Unable to initiate pairing.");
        } finally {
            setPairingDeviceId(null);
        }
    }

    useEffect(() => {
        if (!verification || decision?.localDecision !== 1) return;

        let cancelled = false;
        let running = false;

        const check = async () => {
            if (running) {
                return;
            }

            running = true;

            try {
                const state = await window.remoteLink.getOutgoingDecision(verification.requestId);

                if (cancelled) {
                    return;
                }

                setDecision(state);

                if (state.rejected) {
                    setPairingError("Pairing was rejected.");
                    setVerification(null); return;
                }

                if (state.bothApproved) {
                    const paired = await window.remoteLink.finalizePairing(verification.requestId);

                    if (!cancelled) {
                        setPairedMessage(`${paired.deviceName} paired successfully.`);
                        setVerification(null);
                        setTrusted(await window.remoteLink.getTrustedDevices());
                    }
                }
            } catch (e) {
                if (!cancelled) {
                    setPairingError(e instanceof Error ? e.message : "Unable to refresh pairing status");
                }
            } finally {
                running = false;
            }
        };
        void check();

        const timer = window.setInterval(() => void check(), 2500);

        return () => {
            cancelled = true;
            window.clearInterval(timer);
        };
    }, [verification, decision?.localDecision]);

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

            {pairedMessage &&
                <div className="alert alert-success">
                    {pairedMessage}
                </div>
            }

            {trusted.length > 0 &&
                <section className="mb-4">
                    <h5>Paired Devices</h5>

                    <div className="row g-3">
                        {trusted.filter(d => d.isTrusted).map(d =>
                            <div className="col-md-6" key={d.deviceId}>
                                <div className="content-card">
                                    <strong>{d.deviceName}</strong>

                                    <div className="small text-secondary font-monospace text-break">
                                        {d.fingerprint}
                                    </div>

                                    <span className="badge text-bg-success mt-2">
                                        Trusted
                                    </span>
                                </div>
                            </div>
                        )}
                    </div>
                </section>
            }

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

                    <div className="d-flex gap-2">
                        <button type="button" className="btn btn-primary" disabled={decisionBusy || (decision?.localDecision ?? 0) !== 0} onClick={() => void chooseDecision(true)}>
                            Confirm Match
                        </button>

                        <button type="button" className="btn btn-outline-danger" disabled={decisionBusy || (decision?.localDecision ?? 0) !== 0} onClick={() => void chooseDecision(false)}>
                            Reject
                        </button>
                    </div>

                    {decision?.localDecision === 1 &&
                        <div className="text-secondary mt-3">
                            Approved locally. Waiting for the other computer...
                        </div>
                    }
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
                                    <button type="button" className="btn btn-primary btn-sm" disabled={!device.isOnline || pairingDeviceId !== null || verification !== null || trusted.some(d => d.deviceId === device.id && d.isTrusted)} onClick={() => {
                                        void handlePair(device)
                                    }}>
                                        {trusted.some(d => d.deviceId === device.id && d.isTrusted) ? "Already Paired" : pairingDeviceId === device.id ? (
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

