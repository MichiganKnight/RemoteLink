export {};

declare global {
    interface Window {
        remoteLink: {
            platform: string;
            electronVersion: string;
            appName: string;
        }
    }
}