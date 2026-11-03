const { spawn } = require("node:child_process");
const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");

const HOST_PORT = 5017;
const HOST_URL = `http://127.0.0.1:${HOST_PORT}`;

let hostProcess = null;
let shutdownToken = null;

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

        return;
    }

    const executablePath = getHostExecutablePAth(app);

    if (!fs.existsSync(executablePath)) {
        console.error("[HostManager] Executable missing:", executablePath);

        return;
    }

    const token = crypto.randomBytes(32).toString("hex");

    const child = spawn(executablePath, [], {
        cwd: path.dirname(executablePath),
        windowsHide: true,
        stdio: ["ignore", "pipe", "pipe"],
        env: {
            ...process.env,
            ASPNETCORE_URLS: HOST_URL,
            ASPNETCORE_ENVIRONMENT: app.isPackaged ? "Production" : "Development",
            REMOTELINK_SHUTDOWN_TOKEN: token
        }
    });

    hostProcess = child;
    shutdownToken = token;

    child.stdout?.on("data", data => {
        console.log(`[RemoteLink.Host] ${data.toString().trimEnd()}`);
    });

    child.stderr?.on("data", data => {
        console.error(`[RemoteLink.Host] ${data.toString().trimEnd()}`);
    });

    child.on("error", error => {
        console.error("[HostManager] Startup error:", error);
    });

    child.on("exit", (code, signal) => {
        console.log(`[HostManager] Exited: ${code}, ${signal}`);

        if (hostProcess === child) {
            hostProcess = null;
            shutdownToken = null;
        }
    });

    console.log(`[HostManager] Started Host PID ${child.pid}`);
}

function waitForExit(child, timeoutMs) {
    if (child.exitCode !== null || child.signalCode !== null) {
        return Promise.resolve(true);
    }

    return new Promise(resolve => {
        let finished = false;

        const timeout = setTimeout(() => {
            finish(false);
        }, timeoutMs);

        function finish(exited) {
            if (finished) {
                return;
            }

            finished = true;
            clearTimeout(timeout);
            child.removeListener("exit", onExit);
            resolve(exited);
        }

        function onExit() {
            finish(true);
        }

        child.once("exit", onExit);
    });
}

async function stopHost() {
    const child = hostProcess;
    const token = shutdownToken;

    if (!child || !token) {
        return;
    }

    if (child.exitCode !== null || child.signalCode !== null) {
        return;
    }

    console.log("[HostManager] Requesting graceful shutdown.");

    const controller = new AbortController();

    const timeout = setTimeout(() => controller.abort(), 3000);

    try {
        const response = await fetch(`${HOST_URL}/api/lifecycle/shutdown`, {
            method: "POST",
            headers: {
                "X-RemoteLink-Shutdown-Token": token
            },
            signal: controller.signal
        });

        if (!response.ok) {
            console.warn(`[HostManager] Shutdown returned ${response.status}`);
        }
    } catch (error) {
        console.warn("[HostManager] Graceful shutdown request failed:", error.message);
    } finally {
        clearTimeout(timeout);
    }

    const exited = await waitForExit(child, 5000);

    if (!exited && child.pid) {
        console.warn("[HostManager] Host did not exit. Terminating owned process.");

        const killer = spawn("taskkill", ["/PID", String(child.pid), "/T", "/F"], {
            windowsHide: true,
            stdio: "ignore"
        });

        killer.on("error", error => {
            console.error("[HostManager] Forced shutdown failed:", error);
        });
    }
}

module.exports = {
    HOST_URL,
    startHost,
    stopHost
};