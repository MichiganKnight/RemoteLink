const { spawn, spawnSync } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

const HOST_PORT = 5017;
const HOST_URL = `http://127.0.0.1:${HOST_PORT}`;

let hostProcess = null;
let ownsHostProcess = false;

function getHostExecutablePAth(app) {
    if (app.isPackaged) {
        return path.join(process.resourcesPath, "Host", "RemoteLink.Host.exe");
    }

    return path.resolve(__dirname, "../../../Artifacts/Host/RemoteLink.Host.exe")
}

async function isHostRunning() {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 1500);

    try {
        const response = await fetch(`${HOST_URL}/api/system/status`, {
            signal: controller.signal
        });

        if (!response.ok) {
            return false;
        }

        const data = await response.json();

        return (data.application === "RemoteLink" && data.status === "Online");
    } catch {
        return false;
    } finally {
        clearTimeout(timeout);
    }
}

async function startHost(app) {
    if (hostProcess) {
        return
    }

    if (await isHostRunning()) {
        console.log("[HostManager] Existing Host detected.");

        ownsHostProcess = false;

        return;
    }

    const executablePath = getHostExecutablePAth(app);

    if (!fs.existsSync(executablePath)) {
        console.error("[HostManager] Host executable not found: ", executablePath);

        return;
    }

    console.log("[HostManager] Starting Host: ", executablePath);

    const child = spawn(executablePath, [], {
        cwd: path.dirname(executablePath),
        windowsHide: true,
        stdio: ["ignore", "pipe", "pipe"],
        env: {
            ...process.env,
            ASPNETCORE_URLS: HOST_URL,
            ASPNETCORE_ENVIRONMENT: app.isPackaged ? "Production" : "Development"
        }
    });

    hostProcess = child;
    ownsHostProcess = true;

    child.stdout?.on("data", data => {
        console.log(`[RemoteLink.Host] ${data.toString().trimEnd()}`);
    });

    child.stderr?.on("data", data => {
        console.error(`[RemoteLink.Host] ${data.toString().trimEnd()}`);
    });

    child.on("error", error => {
        console.error("[HostManager] Failed to start Host:", error);
    });

    child.on("exit", (code, signal) => {
        console.log(`[HostManager] Host exited. Code=${code}, Signal=${signal}`);

        if (hostProcess === child) {
            hostProcess = null;
            ownsHostProcess = false;
        }
    });
}

function stopHost() {
    if (!hostProcess || !ownsHostProcess) {
        return;
    }

    const pid = hostProcess.pid;

    if (!pid || hostProcess.exitCode !== null) {
        return;
    }

    console.log(`[HostManager] Stopping owned Host PID ${pid}`);

    if (process.platform === "win32") {
        const result = spawnSync("taskkill", ["/PID", String(pid), "/T", "/F"], {
            windowsHide: true,
            timeout: 5000
        });

        if (result.error) {
            console.error("[HostManager] Stop failed:", result.error);
        }
    } else {
        hostProcess.kill();
    }
}

module.exports = {
    HOST_URL,
    startHost,
    stopHost
};