const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("remoteLink", {
    platform: process.platform,
    electronVersion: process.versions.electron,
    appName: "RemoteLink",

    getHostStatus: () => ipcRenderer.invoke("host:get-status"),
    getDevices: () => ipcRenderer.invoke("devices:get-all")
});