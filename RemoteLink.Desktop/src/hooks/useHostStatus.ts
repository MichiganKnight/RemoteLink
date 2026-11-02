import { useEffect, useState } from "react";

import type { HostStatus } from "../types/electron";

export function useHostStatus() {
    const [hostStatus, setHostStatus] = useState<HostStatus | null>(null);

    const [loading, setLoading] = useState(true);

    useEffect(() => {
        let active = true;
        let checking = false;

        async function checkStatus() {
            if (checking) {
                return;
            }

            checking = true;

            try {
                const result = await window.remoteLink.getHostStatus();

                if (active) {
                    setHostStatus(result);
                }
            } catch (error) {
                if (active) {
                    setHostStatus({
                        connected: false,
                        application: "RemoteLink",
                        status: "Offline",
                        machineName: null,
                        version: null,
                        timestampUtc: null,
                        error: error instanceof Error ? error.message : "Unknown error"
                    });
                }
            } finally {
                checking = false;

                if (active) {
                    setLoading(false);
                }
            }
        }

        void checkStatus();

        const interval = setInterval(() => {
            void checkStatus();
        }, 5000);

        return () => {
            active = false;
            clearInterval(interval);
        };
    }, []);

    return {
        hostStatus,
        loading
    };
}