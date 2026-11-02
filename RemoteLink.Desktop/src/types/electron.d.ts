export {};

export interface HostStatus {
    connected: boolean;
    application: string;
    status: string;
    machineName: string | null;
    version: string | null;
    timestampUtc: string | null;
    error: string | null;
}

declare global {
    interface Window {
        remoteLink: {
            platform: string;
            electronVersion: string;
            appName: string;

            getHostStatus: () => Promise<HostStatus>;
        }
    }
}