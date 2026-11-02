import { useState } from 'react'

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

    const currentPage = navigation.find(item => item.id === activePage);

    return (
        <div className="app-shell">
            <aside className="app-sidebar">
                <div className="app-brand">
                    <i className="bi bi-pc-display-horizontal" />
                    <span>RemoteLink</span>
                </div>

                <div className="sidebar-label">
                    WORKSPACE
                </div>

                <nav className="app-navigation">
                    {navigation.map(item => (
                        <button key={item.id} type="button" className={"navigation-item " + (activePage === item.id ? "active" : "")} onClick={() => setActivePage(item.id)}>
                            <i className={`bi ${item.icon}`} />
                            <span>{item.label}</span>
                        </button>
                    ))}
                </nav>

                <div className="sidebar-footer">
                    <i className="bi bi-circle-fill text-secondary" />
                    <span>Not Connected</span>
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
                        <Dashboard />
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

function Dashboard() {
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
                            <i className={`bi ${feature.icon} feature-icon`} />

                            <h5>{feature.title}</h5>

                            <p className="text-secondary mb-0">
                                {feature.description}
                            </p>
                        </div>
                    </div>
                ))}
            </div>

            <div className="content-card mt-4">
                <h6>Application Information</h6>

                <div className="small text-secondary">
                    <div>
                        Platform: {window.remoteLink.platform}
                    </div>

                    <div>
                        Electron: {window.remoteLink.electronVersion}
                    </div>

                    <div>
                        Application: {window.remoteLink.appName}
                    </div>
                </div>
            </div>
        </>
    );
}

export default App;