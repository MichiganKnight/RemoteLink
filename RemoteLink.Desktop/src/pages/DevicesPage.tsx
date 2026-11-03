import { useEffect, useState } from "react";

import type { DiscoveredDevice } from "../types/electron";

export default function DevicesPage() {
    const [devices, setDevices] = useState<DiscoveredDevice[]>([]);

    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

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

    return (
        <div>
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
                    <i className="bi bi-exclamation-triangle me-2" />
                    {error}
                </div>
            )}

            {loading ? (
                <div className="content-card">
                    Searching for devices...
                </div>
            ) : devices.length === 0 ? (
                <div className="content-card text-center py-5">
                    <i className="bi bi-pc-display fs-1 text-secondary" />

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
                                    <i className="bi bi-pd-display fs-2 text-primary" />

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
                            </div>
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
}

