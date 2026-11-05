const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("remoteLink", {
    platform: process.platform,
    electronVersion: process.versions.electron,
    appName: "RemoteLink",

    getTrustedDevices: () => ipcRenderer.invoke("pairing:trusted"),
    decideIncomingPairing: (requestId, approved) => ipcRenderer.invoke("pairing:incoming-decision", requestId, approved),
    decideOutgoingPairing: (requestId, approved) => ipcRenderer.invoke("pairing:outgoing-decision", requestId, approved),
    getIncomingDecision: (requestId) => ipcRenderer.invoke("pairing:incoming-status", requestId),
    getOutgoingDecision: (requestId) => ipcRenderer.invoke("pairing:outgoing-status", requestId),
    finalizePairing: (requestId) => ipcRenderer.invoke("pairing:finalize", requestId),
    getHostStatus: () => ipcRenderer.invoke("host:get-status"),
    getDevices: () => ipcRenderer.invoke("devices:get-all"),
    getIncomingPairingRequests: () => ipcRenderer.invoke("pairing:get-incoming"),

    startPairing: (request) => ipcRenderer.invoke("pairing:start", request),
    verifyPairing: (request) => ipcRenderer.invoke("pairing:verify", request),
    startDiscoveredPairing: (deviceId) => ipcRenderer.invoke("pairing:start-discovered", deviceId),
    getLocalPairingVerification: (requestId) => ipcRenderer.invoke("pairing:get-local-verification", requestId),


    getChatConversations: () => ipcRenderer.invoke("chat:get-conversations"),
    openChatConversation: (deviceId) => ipcRenderer.invoke("chat:open-conversation", deviceId),
    getChatMessages: (deviceId, beforeUtc = null, beforeId = null, limit = 50) => ipcRenderer.invoke("chat:get-messages", deviceId, beforeUtc, beforeId, limit),
    sendChatMessage: (deviceId, content) => ipcRenderer.invoke("chat:send-message", deviceId, content),
    markRead: (deviceId) => ipcRenderer.invoke("chat:mark-read", deviceId),

    onChatConversationChanged: (callback) => {
        if (typeof callback !== "function") {
            throw new TypeError("Callback must be a function.");
        }

        const listener = (_event, data) => {
            callback(data);
        };

        ipcRenderer.on("chat:conversation-changed", listener);

        return () => {
            ipcRenderer.removeListener("chat:conversation-changed", listener);
        };
    },

    selectTransferFile: () => ipcRenderer.invoke("transfers:select-file"),
    getTransfers: () => ipcRenderer.invoke("transfers:get-all"),
    sendTransferFile: (deviceId, sourcePath) => ipcRenderer.invoke("transfers:send-file", deviceId, sourcePath),
    decideTransfer: (transferId, accepted) => ipcRenderer.invoke("transfers:decide", transferId, accepted),
});