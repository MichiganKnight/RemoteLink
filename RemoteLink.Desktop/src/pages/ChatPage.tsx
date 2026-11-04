import { useCallback, useEffect, useRef, useState } from "react";

import type { ChatConversation, ChatMessage, TrustedDevice } from "../types/electron";

const PAGE_SIZE = 50;

function formatTime(value: string) {
    return new Date(value).toLocaleTimeString([], {
        hour: "numeric",
        minute: "2-digit"
    });
}

function formatDate(value: string) {
    return new Date(value).toLocaleDateString([], {
        weekday: "long",
        month: "long",
        day: "numeric",
        year: "numeric"
    });
}

function dateKey(value: string) {
    const date = new Date(value);

    return `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
}

function messageStatus(status: number) {
    switch (status) {
        case 0:
            return "Pending";
        case 1:
            return "Sent";
        case 2:
            return "Delivered";
        case 3:
            return "Read";
        case 4:
            return "Failed";
        default:
            return "Unknown";
    }
}

function errorMessage(error: unknown) {
    return error instanceof Error ? error.message : "An unexpected error occurred.";
}

export default function ChatPage() {
    const [trusted, setTrusted] = useState<TrustedDevice[]>([]);
    const [conversations, setConversations] = useState<ChatConversation[]>([]);
    const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(null);
    const [messages, setMessages] = useState<ChatMessage[]>([]);
    const [draft, setDraft] = useState("");
    const [search, setSearch] = useState("");

    const [loading, setLoading] = useState(true);
    const [loadingMessages, setLoadingMessages] = useState(false);
    const [loadingOlder, setLoadingOlder] = useState(false);
    const [sending, setSending] = useState(false);
    const [hasOlder, setHasOlder] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const messagesEndRef = useRef<HTMLDivElement | null>(null);
    const selectedDeviceRef = useRef<string | null>(null);
    const messagesRef = useRef<ChatMessage[]>([]);
    const requestVersionRef = useRef(0);

    const selectedDevice = trusted.find(device => device.deviceId === selectedDeviceId);

    const selectedConversation = conversations.find(conversation => conversation.remoteDeviceId === selectedDeviceId);

    const visibleDevices = trusted.filter(device => device.isTrusted)
        .filter(device => device.deviceName.toLowerCase().includes(search.toLowerCase()))
        .sort((a, b) => {
            const aTime = conversations.find(c => c.remoteDeviceId === a.deviceId)?.lastMessageUtc;
            const bTime = conversations.find(c => c.remoteDeviceId === b.deviceId)?.lastMessageUtc;

            return new Date(bTime ?? 0).getTime() - new Date(aTime ?? 0).getTime();
        });

    const refreshConversations = useCallback(async () => {
        const [devices, chats] = await Promise.all([
            window.remoteLink.getTrustedDevices(),
            window.remoteLink.getChatConversations()
        ]);

        setTrusted(devices);
        setConversations(chats);
    }, []);

    useEffect(() => {
        let active = true;

        async function load() {
            try {
                const [devices, chats] = await Promise.all([
                    window.remoteLink.getTrustedDevices(),
                    window.remoteLink.getChatConversations()
                ]);

                if (!active) {
                    return;
                }

                setTrusted(devices);
                setConversations(chats);
                setError(null);
            } catch (error) {
                if (active) {
                    setError(errorMessage(error));
                }
            } finally {
                if (active) {
                    setLoading(false);
                }
            }
        }

        void load();

        return () => {
            active = false;
        };
    }, []);

    useEffect(() => {
        const interval = window.setInterval(() => {
            void refreshConversations().catch(error => {
                console.error("Unable to refresh chat conversations:", error);
            });
        }, 5000);

        return () => window.clearInterval(interval);
    }, [refreshConversations]);

    useEffect(() => {
        selectedDeviceRef.current = selectedDeviceId;
    }, [selectedDeviceId]);

    useEffect(() => {
        messagesRef.current = messages;
    }, [messages]);

    async function selectDevice(deviceId: string) {
        const version = ++requestVersionRef.current;

        selectedDeviceRef.current = deviceId;
        setSelectedDeviceId(deviceId);
        setMessages([]);
        messagesRef.current = [];
        setHasOlder(false);
        setLoadingMessages(true);
        setError(null);

        try {
            await window.remoteLink.openChatConversation(deviceId);

            const history = await window.remoteLink.getChatMessages(deviceId, null, null, PAGE_SIZE);

            if (requestVersionRef.current !== version) {
                return;
            }

            setMessages(history);
            setHasOlder(history.length === PAGE_SIZE);

            window.requestAnimationFrame(() => {
                messagesEndRef.current?.scrollIntoView({
                    behavior: "instant",
                    block: "end"
                });
            });

            await refreshConversations();
        } catch (error) {
            if (requestVersionRef.current === version) {
                setError(errorMessage(error));
            }
        } finally {
            if (requestVersionRef.current === version) {
                setLoadingMessages(false);
            }
        }
    }

    async function loadOlderMessages() {
        if (!selectedDeviceId || loadingOlder || !hasOlder || messages.length === 0) {
            return;
        }

        const deviceId = selectedDeviceId;
        const version = requestVersionRef.current;
        const oldest = messages[0];

        setLoadingOlder(true);
        setError(null);

        try {
            const older = await window.remoteLink.getChatMessages(deviceId, oldest.createdUtc, oldest.id, PAGE_SIZE);

            if (requestVersionRef.current !== version) {
                return;
            }

            setMessages(current => {
                const existing = new Set(current.map(message => message.id));

                return [
                    ...older.filter(message => !existing.has(message.id)),
                    ...current
                ];
            });

            setHasOlder(older.length === PAGE_SIZE);
        } catch (error) {
            if (requestVersionRef.current === version) {
                setError(errorMessage(error));
            }
        } finally {
            if (requestVersionRef.current === version) {
                setLoadingOlder(false);
            }
        }
    }

    async function sendMessage() {
        const content = draft.trim();
        const deviceId = selectedDeviceId;

        if (!deviceId || !content || sending || content.length > 10000) {
            return;
        }

        setSending(true);
        setError(null);

        try {
            const message = await window.remoteLink.sendChatMessage(deviceId, content);

            if (selectedDeviceRef.current === deviceId) {
                setMessages(current => current.some(existing => existing.id === message.id) ? current : [...current, message]);

                setDraft("");

                window.requestAnimationFrame(() => {
                    messagesEndRef.current?.scrollIntoView({
                        behavior: "smooth",
                        block: "end"
                    });
                });
            }

            await refreshConversations();
        } catch (error) {
            setError(errorMessage(error));
        } finally {
            setSending(false);
        }
    }

    return (
        <div className="chat-layout">
            <aside className="chat-conversations content-card">
                <div className="mb-3">
                    <h5 className="mb-1">Conversations</h5>

                    <p className="text-secondary small mb-0">
                        Your paired computers
                    </p>
                </div>

                <div className="input-group mb-3">
                    <span className="input-group-text">
                        <i className="bi bi-search" />
                    </span>

                    <input type="search" className="form-control" placeholder="Search devices..." value={search} onChange={event => setSearch(event.target.value)} />
                </div>

                {loading ? (
                    <div className="text-secondary small">
                        Loading conversations...
                    </div>
                ) : visibleDevices.length === 0 ? (
                    <div className="text-secondary small">
                        No paired devices found.
                    </div>
                ) : (
                    <div className="d-flex flex-column gap-2">
                        {visibleDevices.map(device => {
                            const conversation = conversations.find(c => c.remoteDeviceId === device.deviceId);

                            return (
                                <button key={device.deviceId} type="button" className={`chat-conversation-item text-start ${selectedDeviceId === device.deviceId ? "active" : ""}`} onClick={() => void selectDevice(device.deviceId)}>
                                    <div className="d-flex align-items-center gap-2 text-secondary">
                                        <i className="bi bi-pc-display" />

                                        <strong className="text-truncate flex-grow-1">
                                            {device.deviceName}
                                        </strong>

                                        {(conversation?.unreadCount ?? 0) > 0 && (
                                            <span className="badge text-bg-primary">
                                                {conversation?.unreadCount}
                                            </span>
                                        )}
                                    </div>

                                    <div className="small text-secondary text-truncate mt-1">
                                        {conversation?.lastMessagePreview ?? "Start a conversation"}
                                    </div>

                                    {conversation?.lastMessageUtc && (
                                        <div className="small text-secondary mt-1">
                                            {formatTime(conversation.lastMessageUtc)}
                                        </div>
                                    )}
                                </button>
                            )
                        })}
                    </div>
                )}
            </aside>

            <section className="chat-panel content-card">
                {!selectedDeviceId ? (
                    <div className="chat-placeholder">
                        <i className="bi bi-chat-dots fs-1 text-secondary" />

                        <h5 className="mt-3">Select a conversation</h5>

                        <p className="text-secondary">
                            Choose a paired computer to view messages or start chatting.
                        </p>
                    </div>
                ) : (
                    <>
                        <div className="chat-header">
                            <div className="d-flex align-items-center gap-3">
                                <i className="bi bi-pc-display fs-4 text-primary" />

                                <div>
                                    <h6 className="mb-1">
                                        {selectedDevice?.deviceName ?? selectedConversation?.remoteDeviceName ?? "Unknown Device"}
                                    </h6>

                                    <span className="small text-secondary">
                                        Paired Device
                                    </span>
                                </div>
                            </div>
                        </div>

                        {error && (
                            <div className="alert alert-danger m-3 mb-0">
                                {error}
                            </div>
                        )}

                        <div className="chat-messages">
                            {loadingMessages ? (
                                <div className="text-center text-secondary py-4">
                                    Loading messages...
                                </div>
                            ) : (
                                <>
                                    {hasOlder && (
                                        <div className="text-center mb-3">
                                            <button type="button" className="btn btn-outline-secondary btn-sm" disabled={loadingOlder} onClick={() => void loadOlderMessages()}>
                                                {loadingOlder ? "Loading..." : "Load older messages"}
                                            </button>
                                        </div>
                                    )}

                                    {messages.length === 0 && (
                                        <div className="text-center text-secondary py-5">
                                            No messages yet. Start the conversation below.
                                        </div>
                                    )}

                                    {messages.map((message, index) => {
                                        const previous = messages[index - 1];
                                        const showDate = !previous || dateKey(previous.createdUtc) !== dateKey(message.createdUtc);

                                        const outgoing = message.direction === 1;

                                        return (
                                            <div key={message.id}>
                                                {showDate && (
                                                    <div className="chat-date-separator">
                                                        {formatDate(message.createdUtc)}
                                                    </div>
                                                )}

                                                <div className={`chat-message-row ${outgoing ? "outgoing" : "incoming"}`}>
                                                    <div className={`chat-message-bubble ${outgoing ? "outgoing" : "incoming"}`}>
                                                        <div className="chat-message-content">
                                                            {message.content}
                                                        </div>

                                                        <div className="chat-message-meta">
                                                            <span>
                                                                {formatTime(message.createdUtc)}
                                                            </span>

                                                            {outgoing && (
                                                                <span>
                                                                    {messageStatus(message.status)}
                                                                </span>
                                                            )}
                                                        </div>
                                                    </div>
                                                </div>
                                            </div>
                                        )
                                    })}

                                    <div ref={messagesEndRef} />
                                </>
                            )}
                        </div>

                        <div className="chat-composer">
                            <textarea className="form-control" rows={2} placeholder="Type a message..." value={draft} maxLength={10000} disabled={loadingMessages || sending} onChange={event => setDraft(event.target.value)} onKeyDown={event => {
                                if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
                                    event.preventDefault();

                                    void sendMessage();
                                }
                            }} />

                            <button type="button" className="btn btn-primary" disabled={!draft.trim() || sending || loadingMessages} onClick={() => void sendMessage()}>
                                {sending ? (
                                    <span className="spinner-border spinner-border-sm"/>
                                ) : (
                                    <i className="bi bi-send"/>
                                )}
                            </button>
                        </div>
                    </>
                )}
            </section>
        </div>
    );
}