const { app, BrowserWindow, ipcMain } = require("electron");
const path = require("node:path");

const { HOST_URL, startHost, stopHost } = require("./services/hostManager.cjs");

const isDevelopment = process.env.ELECTRON_DEV === "1";

const gotSingleInstanceLock = app.requestSingleInstanceLock();

if (!gotSingleInstanceLock) {
    app.quit();
} else {
    app.on("second-instance", () => {
        const window = BrowserWindow.getAllWindows()[0];

        if (window) {
            if (window.isMinimized()) {
                window.restore();
            }

            window.focus();
        }
    });

    initializeApplication();
}

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

    ipcMain.handle("devices:get-all", async () => {
        const controller = new AbortController();

        const timeout = setTimeout(() => controller.abort(), 3000);

        try {
            const response = await fetch(`${HOST_URL}/api/devices`, {
                signal: controller.signal,
                headers: {
                    Accept: "application/json"
                }
            });

            if (!response.ok) {
                throw new Error(`Devices API returned HTTP ${response.status}`);
            }

            return await response.json();
        } finally {
            clearTimeout(timeout);
        }
    });

    ipcMain.handle("pairing:get-incoming", async () => {
        const response = await fetch("http://127.0.0.1:5017/api/local/pairing/incoming", {
            signal: AbortSignal.timeout(5000)
        });

        if (!response.ok) {
            throw new Error(`Failed to load pairing requests: ${response.status}`);
        }

        return await response.json();
    });

    ipcMain.handle("pairing:start", async (_event, request) => {
        const response = await fetch(`${HOST_URL}/api/local/pairing/start`, {
            method: "POST",
            signal: AbortSignal.timeout(15000),
            headers: {
                "Content-Type": "application/json",
                Accept: "application/json"
            },
            body: JSON.stringify(request)
        });

        if (!response.ok) {
            const message = await response.text();

            throw new Error(`Pairing request failed (${response.status}): ${message}`);
        }

        return await response.json();
    });

    ipcMain.handle("pairing:verify", async (_event, request) => {
        const response = await fetch(`${HOST_URL}/api/local/pairing/verify`, {
            method: "POST",
            signal: AbortSignal.timeout(15000),
            headers: {
                "Content-Type": "application/json",
                Accept: "application/json"
            },
            body: JSON.stringify(request)
        });

        if (!response.ok) {
            const message = await response.text();

            throw new Error(`Pairing verification failed (${response.status}): ${message}`);
        }

        return await response.json();
    });

    ipcMain.handle("pairing:start-discovered", async (_event, deviceId) => {
        if (typeof deviceId !== "string" || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(deviceId)) {
            throw new Error("Invalid device ID.");
        }

        const response = await fetch(`${HOST_URL}/api/local/pairing/start-discovered/${deviceId}`, {
            method: "POST",
            signal: AbortSignal.timeout(30000),
            headers: {
                Accept: "application/json"
            }
        });

        if (!response.ok) {
            const message = await response.text();

            throw new Error(`Unable to start pairing (${response.status}): ${message}`);
        }

        return await response.json();
    });

    ipcMain.handle("pairing:get-local-verification", async (_event, requestId) => {
        if (typeof requestId !== "string" || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(requestId)) {
            throw new Error("Invalid pairing request ID.");
        }

        const response = await fetch(`${HOST_URL}/api/local/pairing/incoming/${requestId}/verification`, {
            signal: AbortSignal.timeout(5000),
            headers: {
                Accept: "application/json"
            }
        });

        if (!response.ok) {
            throw new Error(`Verification unavailable (${response.status})`);
        }

        return await response.json();
    });
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
        mainWindow.loadURL("http://127.0.0.1/5173");

        mainWindow.webContents.openDevTools({
            mode: "detach"
        });

        mainWindow.maximize();
    } else {
        mainWindow.loadFile(path.join(__dirname, "../dist/index.html"));

        mainWindow.maximize();
    }
}

function initializeApplication() {
    app.whenReady().then(async () => {
        registerIpcHandlers();

        createWindow();

        await startHost(app);

        app.on("activate", () => {
            if (BrowserWindow.getAllWindows().length === 0) {
                createWindow();
            }
        });
    });
}

let shutdownStarted = false;
let shutdownCompleted = false;

app.on("before-quit", event => {
    if (shutdownCompleted) {
        return;
    }

    event.preventDefault();

    if (shutdownStarted) {
        return;
    }

    shutdownStarted = true;

    void (async () => {
        try {
            await stopHost();
        } catch (error) {
            console.error("[RemoteLink] Shutdown error:", error);
        } finally {
            shutdownCompleted = true;

            app.quit();
        }
    })();
});

app.on("window-all-closed", () => {
    app.quit();
});