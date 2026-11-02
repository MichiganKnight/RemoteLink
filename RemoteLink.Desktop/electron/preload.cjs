const { contextBridge } = require("electron");

contextBridge.exposeInMainWorld("remoteLink", {
    platform: process.platform,
    electronVersion: process.versions.electron,
    appName: "RemoteLink"
});