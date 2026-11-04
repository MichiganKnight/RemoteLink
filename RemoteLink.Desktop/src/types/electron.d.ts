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

export interface PairingApprovalState {
    requestId: string;
    remoteDeviceId: string;
    localDecision: number;
    remoteDecision: number;
    bothApproved: boolean;
    rejected: boolean;
    expiresUtc: string;
}

export interface TrustedDevice {
    deviceId: string;
    deviceName: string;
    publicKey: string;
    fingerprint: string;
    pairedUtc: string;
    lastConnectedUtc: string;
    isTrusted: boolean;
}

export type ChatMessageStatus = 0 | 1 | 2 | 3 | 4;
export type ChatMessageDirection = 0 | 1;

export interface ChatConversation {
    id: string;
    remoteDeviceId: string;
    remoteDeviceName: string;
    createdUtc: string;
    lastMessageUtc: string | null;
    lastReadUtc: string | null;
    lastMessagePreview: string | null;
    unreadCount: number;
}

export interface ChatMessage {
    id: string;
    conversationId: string;
    senderDeviceId: string;
    recipientDeviceId: string;
    content: string;
    direction: ChatMessageDirection;
    status: ChatMessageStatus;
    createdUtc: string;
    sentUtc: string | null;
    deliveredUtc: string | null;
    readUtc: string | null;
}

declare global {
    interface Window {
        remoteLink: {
            platform: string;
            electronVersion: string;
            appName: string;

            getTrustedDevices: () => Promise<TrustedDevice[]>;
            decideIncomingPairing: (requestId: string, approved: boolean) => Promise<PairingApprovalState>;
            decideOutgoingPairing: (requestId: string, approved: boolean) => Promise<PairingApprovalState>;
            getIncomingDecision: (requestId: string) => Promise<PairingApprovalState>;
            getOutgoingDecision: (requestId: string) => Promise<PairingApprovalState>;
            finalizePairing: (requestId: string) => Promise<TrustedDevice>;
            getHostStatus: () => Promise<HostStatus>;
            getDevices: () => Promise<DiscoveredDevice[]>;
            getIncomingPairingRequests: () => Promise<IncomingPairingRequest[]>;

            startPairing: (request: StartPairingRequest) => Promise<PairingProposalResponse>;
            verifyPairing: (request: VerifyPairingRequest) => Promise<PairingHandshakeVerification>;
            startDiscoveredPairing: (deviceId: string) => Promise<PairingHandshakeVerification>;
            getLocalPairingVerification: (requestId: string) => Promise<PairingLocalVerification>;


            getChatConversations: () => Promise<ChatConversation[]>;
            openChatConversation: (deviceId: string) => Promise<ChatConversation>;
            getChatMessages: (deviceId: string, beforeUtc?: string | null, beforeId?: string | null, limit?: number) => Promise<ChatMessage[]>;
            sendChatMessage: (deviceId: string, content: string) => Promise<ChatMessage>;
        }
    }
}