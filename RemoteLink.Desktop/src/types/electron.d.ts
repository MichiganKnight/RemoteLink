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

export interface DiscoveredDevice {
    id: string;
    name: string;
    ipAddress: string;
    discoveryPort: number;
    lastSeenUtc: string;
    isOnline: boolean;
}


export interface IncomingPairingRequest {
    requestId: string;
    deviceId: string;
    deviceName: string;
    publicKey: string;
    fingerprint: string;
    createdUtc: string;
    expiresUtc: string;
    status: number;
}

declare global {
    interface Window {
        remoteLink: {
            platform: string;
            electronVersion: string;
            appName: string;

            getHostStatus: () => Promise<HostStatus>;
            getDevices: () => Promise<DiscoveredDevice[]>;
            getIncomingPairingRequests: () => Promise<IncomingPairingRequest[]>;
        }
    }
}