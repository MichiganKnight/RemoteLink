const { app, BrowserWindow, ipcMain } = require("electron");
const path = require("node:path");

const isDevelopment = process.env.ELECTRON_DEV === "1";

const HOST_URL = "http://127.0.0.1:5017";

async function getHostStatus() {
    const controller = new AbortController();

    const timeout = setTimeout(() => {
        controller.abort();
    }, 3000);

    try {
        const response = await fetch(`${HOST_URL}/api/system/status`, {
            method: "GET",
            signal: controller.signal,
            headers: {
                "Accept": "application/json"
            }
        });

        if (!response.ok) {
            throw new Error(`Host returned HTTP ${response.status}`);
        }

        const data = await response.json();

        if (data.application !== "RemoteLink" || data.status !== "Online") {
            throw new Error("Unexpected Host response");
        }

        return {
            connected: true,
            application: data.application,
            status: data.status,
            machineName: data.machineName,
            version: data.version,
            timestampUtc: data.timestampUtc,
            error: null
        }
    } catch (error) {
        return {
            connected: false,
            application: "RemoteLink",
            status: "Offline",
            machineName: null,
            version: null,
            timestampUtc: null,
            error: error instanceof Error ? error.message : "Unknown connection error"
        };
    } finally {
        clearTimeout(timeout);
    }
}

function registerIpcHandlers() {
    ipcMain.handle("host:get-status", async () => await getHostStatus());
}

function createWindow() {
    const mainWindow = new BrowserWindow({
        title: "RemoteLink",
        width: 1280,
        height: 720,
        minWidth: 950,
        minHeight: 650,
        show: false,
        backgroundColor: "#111827",
        autoHideMenuBar: true,

        webPreferences: {
            preload: path.join(__dirname, "preload.cjs"),
            nodeIntegration: false,
            contextIsolation: true,
            sandbox: true
        }
    });

    mainWindow.once("ready-to-show", () => {
        mainWindow.show();
    });

    if (isDevelopment) {
        mainWindow.loadURL("http://127.0.0.1/5017");

        mainWindow.webContents.openDevTools({
            mode: "detach"
        });

        mainWindow.maximize();
    } else {
        mainWindow.loadFile(path.join(__dirname, "../dist/index.html"));

        mainWindow.maximize();
    }
}

app.whenReady().then(() => {
    registerIpcHandlers();
    createWindow();

    app.on("activate", () => {
        if (BrowserWindow.getAllWindows().length === 0) {
            createWindow();
        }
    });
});

app.on("window-all-closed", () => {
    if (process.platform !== "darwin") {
        app.quit();
    }
});