import { useCallback, useEffect, useState } from "react";

import type { TransferJob, TrustedDevice } from "../types/electron";

function formatBytes(bytes: number): string {
    if (bytes === 0)  {
        return "0 B";
    }

    const units = ["B", "KB", "MB", "GB", "TB"];
    const index = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);

    return `${(bytes / Math.pow(1024, index)).toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}

function transferStatus(status: number): string {
    const statuses = [
        "Pending",
        "Awaiting Approval",
        "Accepted",
        "Transferring",
        "Paused",
        "Completed",
        "Failed",
        "Rejected",
        "Cancelled"
    ];

    return statuses[status] ?? "Unknown";
}

function statusClass(status: number): string {
    switch (status) {
        case 3: return "text-bg-primary";
        case 5: return "text-bg-success";
        case 6:
        case 7:
        case 8: return "text-bg-danger";
        default: return "text-bg-secondary";
    }
}

function errorMessage(error: unknown): string {
    return error instanceof Error ? error.message : "Unexpected error.";
}

export default function TransfersPage() {
    const [devices, setDevices] = useState<TrustedDevice[]>([]);
    const [transfers, setTransfers] = useState<TransferJob[]>([]);

    const [recipientId, setRecipientId] = useState("");
    const [selectedFile, setSelectedFile] = useState<string | null>(null);

    const [loading, setLoading] = useState(true);
    const [sending, setSending] = useState(false);
    const [decidingId, setDecidingId] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);

    const refresh = useCallback(async () => {
        const [trusted, jobs] = await Promise.all([
            window.remoteLink.getTrustedDevices(),
            window.remoteLink.getTransfers()
        ]);

        setDevices(trusted.filter(device => device.isTrusted));
        setTransfers(jobs);
    }, []);

    useEffect(() => {
        let cancelled = false;

        async function loadTransfers() {
            try {
                const [trusted, jobs] = await Promise.all([
                    window.remoteLink.getTrustedDevices(),
                    window.remoteLink.getTransfers()
                ]);

                if (cancelled) return;

                setDevices(trusted.filter(device => device.isTrusted));
                setTransfers(jobs);
                setError(null);
            } catch (error) {
                if (!cancelled) {
                    setError(errorMessage(error));
                }
            } finally {
                if (!cancelled) {
                    setLoading(false);
                }
            }
        }

        void loadTransfers();

        const interval = window.setInterval(() => {
            void loadTransfers();
        }, 3000);

        return () => {
            cancelled = true;
            window.clearInterval(interval);
        };
    }, []);

    async function chooseFile() {
        try {
            const path = await window.remoteLink.selectTransferFile();

            if (path) {
                setSelectedFile(path);
                setError(null);
            }
        } catch (error) {
            setError(errorMessage(error));
        }
    }

    async function sendFile() {
        if (!recipientId || !selectedFile || sending) {
            return;
        }

        setSending(true);
        setError(null);

        try {
            await window.remoteLink.sendTransferFile(recipientId, selectedFile);

            setSelectedFile(null);
            await refresh();
        } catch (error) {
            setError(errorMessage(error));
        } finally {
            setSending(false);
        }
    }

    async function decide(transferId: string, accepted: boolean) {
        setDecidingId(transferId);
        setError(null);

        try {
            await window.remoteLink.decideTransfer(
                transferId,
                accepted
            );

            await refresh();
        } catch (error) {
            setError(errorMessage(error));
            await refresh().catch(console.error);
        } finally {
            setDecidingId(null);
        }
    }

    const incoming = transfers.filter(transfer => transfer.direction === 0 && transfer.status === 1);

    const history = transfers.filter(transfer => !(transfer.direction === 0 && transfer.status === 1));

    function deviceName(deviceId: string): string {
        return devices.find(device => device.deviceId === deviceId)?.deviceName ?? deviceId;
    }

    return (
        <div className="transfers-page">
            {error && (
                <div className="alert alert-danger">
                    {error}
                </div>
            )}

            <div className="row g-4 mb-4">
                <div className="col-xl-6">
                    <div className="content-card h-100">
                        <h5 className="mb-2">
                            <i className="bi bi-send me-2 text-primary" />
                            Send File
                        </h5>

                        <p className="text-secondary small">
                            Send a file securely to a paired computer.
                        </p>

                        <label className="form-label">
                            Destination Device
                        </label>

                        <select className="form-select mb-3" value={recipientId} onChange={event => setRecipientId(event.target.value)}>
                            <option value="">Select a device...</option>

                            {devices.map(device => (
                                <option key={device.deviceId} value={device.deviceId}>
                                    {device.deviceName}
                                </option>
                            ))}
                        </select>

                        <div className="transfer-file-picker mb-3">
                            <i className="bi bi-file-earmark-arrow-up" />

                            <div className="small mt-2 mb-3">
                                {selectedFile ? selectedFile.split(/[\\/]/).pop() : "No file selected"}
                            </div>

                            <button type="button" className="btn btn-outline-primary btn-sm" onClick={() => void chooseFile()}>
                                Browse Files
                            </button>
                        </div>

                        <button type="button" className="btn btn-primary w-100" disabled={!recipientId || !selectedFile || sending} onClick={() => void sendFile()}>
                            {sending ? "Sending Offer..." : "Send File"}
                        </button>

                        <p className="text-secondary small mt-3 mb-0">
                            File contents will transfer after the recipient accepts the request.
                        </p>
                    </div>
                </div>

                <div className="col-xl-6">
                    <div className="content-card h-100">
                        <h5 className="mb-2">
                            <i className="bi bi-inbox me-2 text-primary"/>
                            Incoming Requests
                        </h5>

                        <p className="text-secondary small">
                            Approve files sent by your trusted devices.
                        </p>

                        {incoming.length === 0 ? (
                            <div className="transfer-empty">
                                <i className="bi bi-inbox"/>
                                <span>No pending requests</span>
                            </div>
                        ) : incoming.map(transfer => (
                            <div className="transfer-request" key={transfer.id}>
                                <div className="fw-semibold text-break">
                                    {transfer.name}
                                </div>

                                <div className="text-secondary small mt-1">
                                    From {deviceName(transfer.remoteDeviceId)}
                                </div>

                                <div className="text-secondary small">
                                    {formatBytes(transfer.totalBytes)}
                                </div>

                                <div className="d-flex gap-2 mt-3">
                                    <button className="btn btn-success btn-sm" disabled={decidingId === transfer.id} onClick={() => void decide(transfer.id, true)}>
                                        Accept
                                    </button>

                                    <button className="btn btn-outline-danger btn-sm" disabled={decidingId === transfer.id} onClick={() => void decide(transfer.id, false)}>
                                        Decline
                                    </button>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            </div>

            <div className="content-card">
                <div className="d-flex justify-content-between align-items-center mb-3">
                    <h5 className="mb-0">Transfer History</h5>

                    <button className="btn btn-outline-secondary btn-sm" onClick={() => void refresh()}>
                        <i className="bi bi-arrow-clockwise me-1" />
                        Refresh
                    </button>
                </div>

                {loading ? (
                    <p className="text-secondary">Loading transfers...</p>
                ) : history.length === 0 ? (
                    <div className="transfer-empty">
                        <i className="bi bi-folder2-open"/>
                        <span>No transfers yet</span>
                    </div>
                ) : (
                    <div className="d-flex flex-column gap-3">
                        {history.map(transfer => {
                            const percent = transfer.totalBytes > 0 ? Math.min(100, (transfer.transferredBytes / transfer.totalBytes) * 100) : transfer.status === 5 ? 100 : 0;

                            return (
                                <div className="transfer-history-item" key={transfer.id}>
                                    <div className="d-flex justify-content-between gap-3">
                                        <div className="min-width-0">
                                            <div className="fw-semibold text-break">
                                                <i className={`bi ${transfer.direction === 0 ? "bi-download" : "bi-upload"} me-2`} />
                                                {transfer.name}
                                            </div>

                                            <div className="small text-secondary mt-1">
                                                {transfer.direction === 0 ? "From" : "To"}
                                                {deviceName(transfer.remoteDeviceId)}
                                            </div>
                                        </div>

                                        <span className={`badge align-self-start ${statusClass(transfer.status)}`}>
                                            {transferStatus(transfer.status)}
                                        </span>
                                    </div>

                                    <div className="progress mt-3">
                                        <div className="progress-bar" style={{ width: `${percent}%`}} />
                                    </div>

                                    <div className="d-flex justify-content-between text-secondary small mt-2">
                                        <span>
                                            {formatBytes(transfer.transferredBytes)}
                                            {" / "}
                                            {formatBytes(transfer.totalBytes)}
                                        </span>

                                        <span>{Math.round(percent)}%</span>
                                    </div>

                                    {transfer.errorMessage && (
                                        <div className="text-danger small mt-2">
                                            {transfer.errorMessage}
                                        </div>
                                    )}
                                </div>
                            );
                        })}
                    </div>
                )}
            </div>
        </div>
    )
}