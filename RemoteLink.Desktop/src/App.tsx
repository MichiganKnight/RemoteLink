import { useState } from 'react'

import { useHostStatus } from "./hooks/useHostStatus";

import type { HostStatus } from "./types/electron";

import DevicesPage from "./pages/DevicesPage";
import ChatPage from "./pages/ChatPage";

import "./styles/chat.css";

type Page = "Dashboard" | "Devices" | "Transfers" | "Chat" | "Remote" | "Settings";

interface NavigationItem {
    id: Page;
    label: string;
    icon: string;
}

const navigation: NavigationItem[] = [
    {
        id: "Dashboard",
        label: "Dashboard",
        icon: "bi-speedometer2"
    },
    {
        id: "Devices",
        label: "Devices",
        icon: "bi-pc-display"
    },
    {
        id: "Transfers",
        label: "File Transfers",
        icon: "bi-folder-symlink"
    },
    {
        id: "Chat",
        label: "Chat",
        icon: "bi-chat-dots"
    },
    {
        id: "Remote",
        label: "Remote Desktop",
        icon: "bi-display"
    },
    {
        id: "Settings",
        label: "Settings",
        icon: "bi-gear"
    }
];

function App() {
    const [activePage, setActivePage] = useState<Page>("Dashboard");

    const { hostStatus, loading } = useHostStatus();

    const currentPage = navigation.find(item => item.id === activePage);

    return (
        <div className="app-shell">
            <aside className="app-sidebar">
                <div className="app-brand">
                    <i className="bi bi-pc-display-horizontal"/>
                    <span>RemoteLink</span>
                </div>

                <div className="sidebar-label">
                    WORKSPACE
                </div>

                <nav className="app-navigation">
                    {navigation.map(item => (
                        <button key={item.id} type="button" className={"navigation-item " + (activePage === item.id ? "active" : "")} onClick={() => setActivePage(item.id)}>
                            <i className={`bi ${item.icon}`}/>
                            <span>{item.label}</span>
                        </button>
                    ))}
                </nav>

                <div className="sidebar-footer d-flex align-items-center justify-content-between gap-2">
                    <div className="d-flex align-items-center gap-2">
                        <i className="bi bi-hdd-network"/>

                        <span>Backend Host</span>
                    </div>

                    <span className={`badge rounded-pill ${loading ? "text-bg-secondary" : hostStatus?.connected ? "text-bg-success" : "text-bg-danger"}`}>
                        <i className={`bi ${loading ? "bi-arrow-repeat" : hostStatus?.connected ? "bi-check-circle-fill" : "bi-x-circle-fill"} me-1`}/>

                        {loading ? "Checking" : hostStatus?.connected ? "Online" : "Offline"}
                    </span>
                </div>
            </aside>

            <main className="app-main">
                <header className="app-header">
                    <div>
                        <h4 className="mb-1">
                            {currentPage?.label}
                        </h4>

                        <span className="text-secondary small">
                            RemoteLink Desktop
                        </span>
                    </div>

                    <span className="badge text-bg-secondary">
                        v0.1.0
                    </span>
                </header>

                <div className="app-content">
                    {activePage === "Dashboard" ? (
                        <Dashboard hostStatus={hostStatus} loading={loading}/>
                    ) : activePage === "Devices" ? (
                        <DevicesPage/>
                    ) : activePage === "Chat" ? (
                        <ChatPage/>
                    ) : (
                        <div className="content-card">
                            <h5>{currentPage?.label}</h5>

                            <p className="text-secondary mb-0">
                                This feature will be implemented in an upcoming development phase.
                            </p>
                        </div>
                    )}
                </div>
            </main>
        </div>
    );
}

interface DashboardProps {
    hostStatus: HostStatus | null;
    loading: boolean;
}

function Dashboard({ hostStatus, loading }: DashboardProps) {
    const features = [
        {
            title: "Devices",
            description: "Manage connected computers",
            icon: "bi-pc-display"
        },
        {
            title: "File Transfers",
            description: "Send and receive files",
            icon: "bi-folder-symlink"
        },
        {
            title: "Chat",
            description: "Message connected devices",
            icon: "bi-chat-dots"
        },
        {
            title: "Remote Desktop",
            description: "View and control remote screens",
            icon: "bi-display"
        }
    ];

    return (
        <>
            <div className="content-card mb-4">
                <h5>Welcome to RemoteLink</h5>

                <p className="text-secondary mb-0">
                    Your remote connectivity workspace.
                    Connect devices, transfer files, communicate, and manage remote desktops.
                </p>
            </div>

            <div className="row g-3">
                {features.map(feature => (
                    <div className="col-md-6" key={feature.title}>
                        <div className="content-card feature-card">
                            <i className={`bi ${feature.icon} feature-icon`}/>

                            <h5>{feature.title}</h5>

                            <p className="text-secondary mb-0">
                                {feature.description}
                            </p>
                        </div>
                    </div>
                ))}
            </div>

            <div className="content-card mt-4">
                <div className="d-flex align-items-center justify-content-between mb-3">
                    <h6 className="mb-0">
                        Backend Connection
                    </h6>

                    <span className={"badge " + (hostStatus?.connected ? "text-bg-success" : "text-bg-danger")}>
                        {loading ? "Checking" : hostStatus?.connected ? "Online" : "Offline"}
                    </span>
                </div>

                <div className="small">
                    <div className="mb-2">
                        <span className="text-secondary">
                            Host:
                        </span>{" "} {hostStatus?.machineName ?? "Unavailable"}
                    </div>

                    <div className="mb-2">
                        <span className="text-secondary">
                            Backend Version:
                        </span>{" "} {hostStatus?.version ?? "Unavailable"}
                    </div>

                    <div className="mb-2">
                        <span className="text-secondary">
                            Endpoint:
                        </span>{" "} 127.0.0.1:5017
                    </div>

                    {hostStatus?.error && (
                        <div className="text-danger mt-3">
                            <i className="bi bi-exclamation-triangle me-2"/>
                            {hostStatus.error}
                        </div>
                    )}
                </div>
            </div>
        </>
    );
}

export default App;