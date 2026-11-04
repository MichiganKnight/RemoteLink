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

export interface StartPairingRequest {
    deviceId: string;
    address: string;
    expectedTlsThumbprint: string;
}

export interface PairingProposalResponse {
    requestId: string;
    acceptedForReview: boolean;
    message: string;
}

export interface VerifyPairingRequest {
    requestId: string;
    deviceId: string;
    address: string;
    expectedTlsThumbprint: string;
}

export interface PairingHandshakeVerification {
    requestId: string;
    remoteDeviceId: string;
    remoteDeviceName: string;
    remoteIdentityFingerprint: string;
    verificationCode: string;
    signatureVerified: boolean;
}

export interface PairingLocalVerification {
    requestId: string;
    remoteDeviceId: string;
    remoteDeviceName: string;
    verificationCode: string;
    expiresUtc: string;
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

            startPairing: (request: StartPairingRequest) => Promise<PairingProposalResponse>;
            verifyPairing: (request: VerifyPairingRequest) => Promise<PairingHandshakeVerification>;
            startDiscoveredPairing: (deviceId: string) => Promise<PairingHandshakeVerification>;
            getLocalPairingVerification: (requestId: string) => Promise<PairingLocalVerification>;
        }
    }
}