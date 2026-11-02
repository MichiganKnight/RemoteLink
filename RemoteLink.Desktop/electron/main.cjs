const { app, BrowserWindow } = require("electron");
const path = require("node:path");

const isDevelopment = process.env.ELECTRON_DEV === "1";

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
    } else {
        mainWindow.loadFile(path.join(__dirname, "../dist/index.html"));
    }
}

app.whenReady().then(() => {
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