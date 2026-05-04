import { useEffect, useState, type ReactNode } from "react";
import { MsalProvider } from "@azure/msal-react";
import type { PublicClientApplication } from "@azure/msal-browser";
import { initAuth } from "./msalConfig";

interface Props {
    children: ReactNode;
}

export default function AuthBootstrap({ children }: Props) {
    const [msal, setMsal] = useState<PublicClientApplication | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        initAuth()
            .then(({ msal }) => {
                if (!cancelled) setMsal(msal);
            })
            .catch((err: unknown) => {
                if (!cancelled) {
                    setError(err instanceof Error ? err.message : String(err));
                }
            });
        return () => {
            cancelled = true;
        };
    }, []);

    if (error) {
        return (
            <div className="flex min-h-screen items-center justify-center bg-midnight-950 p-6 text-midnight-100">
                <div className="max-w-md rounded-lg border border-rose-500/40 bg-midnight-900 p-6 shadow-lg">
                    <h1 className="text-lg font-semibold text-rose-300">
                        Sign-in is unavailable
                    </h1>
                    <p className="mt-2 text-sm text-midnight-300">{error}</p>
                    <p className="mt-4 text-xs text-midnight-400">
                        Verify the API is reachable and that <code>AzureAd:TenantId</code> /
                        <code> AzureAd:ClientId</code> are configured.
                    </p>
                </div>
            </div>
        );
    }

    if (!msal) {
        return (
            <div className="flex min-h-screen items-center justify-center bg-midnight-950 text-midnight-300">
                <p className="text-sm">Loading sign-in…</p>
            </div>
        );
    }

    return <MsalProvider instance={msal}>{children}</MsalProvider>;
}
