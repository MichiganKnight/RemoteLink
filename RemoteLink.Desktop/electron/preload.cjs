const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("remoteLink", {
    platform: process.platform,
    electronVersion: process.versions.electron,
    appName: "RemoteLink",

    getHostStatus: () => ipcRenderer.invoke("host:get-status"),
    getDevices: () => ipcRenderer.invoke("devices:get-all"),
    getIncomingPairingRequests: () => ipcRenderer.invoke("pairing:get-incoming"),

    startPairing: (request) => ipcRenderer.invoke("pairing:start", request),
    verifyPairing: (request) => ipcRenderer.invoke("pairing:verify", request),
    startDiscoveredPairing: (deviceId) => ipcRenderer.invoke("pairing:start-discovered", deviceId)
});