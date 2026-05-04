import {
    PublicClientApplication,
    type Configuration,
    type AccountInfo,
    InteractionRequiredAuthError,
} from "@azure/msal-browser";

export interface AuthConfig {
    authority: string;
    tenantId: string;
    clientId: string;
    apiScope: string;
}

let _config: AuthConfig | null = null;
let _msal: PublicClientApplication | null = null;
let _initPromise: Promise<void> | null = null;

const BASE = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/$/, "");

async function fetchAuthConfig(): Promise<AuthConfig> {
    const res = await fetch(`${BASE}/api/config`);
    if (!res.ok) {
        throw new Error(`Failed to load /api/config (HTTP ${res.status})`);
    }
    const data = (await res.json()) as AuthConfig;
    if (!data.clientId || !data.tenantId) {
        throw new Error(
            "Entra ID is not configured on the server (missing TenantId/ClientId)."
        );
    }
    return data;
}

export async function initAuth(): Promise<{ msal: PublicClientApplication; config: AuthConfig }> {
    if (_initPromise && _msal && _config) {
        await _initPromise;
        return { msal: _msal, config: _config };
    }

    _initPromise = (async () => {
        _config = await fetchAuthConfig();

        const msalConfig: Configuration = {
            auth: {
                clientId: _config.clientId,
                authority: _config.authority,
                redirectUri: window.location.origin,
                postLogoutRedirectUri: window.location.origin,
            },
            cache: {
                cacheLocation: "sessionStorage",
                storeAuthStateInCookie: false,
            },
        };

        _msal = new PublicClientApplication(msalConfig);
        await _msal.initialize();

        // Handle redirect response if we just came back from login
        const result = await _msal.handleRedirectPromise();
        if (result?.account) {
            _msal.setActiveAccount(result.account);
        } else {
            const accounts = _msal.getAllAccounts();
            if (accounts.length > 0 && !_msal.getActiveAccount()) {
                _msal.setActiveAccount(accounts[0]);
            }
        }
    })();

    await _initPromise;
    return { msal: _msal!, config: _config! };
}

export function getMsalInstance(): PublicClientApplication {
    if (!_msal) {
        throw new Error("MSAL has not been initialized. Call initAuth() first.");
    }
    return _msal;
}

export function getAuthConfig(): AuthConfig {
    if (!_config) {
        throw new Error("Auth config has not been loaded. Call initAuth() first.");
    }
    return _config;
}

export function getActiveAccount(): AccountInfo | null {
    return _msal?.getActiveAccount() ?? null;
}

/**
 * Acquire a bearer access token for the API. Falls back to interactive
 * redirect when silent acquisition fails (e.g. consent / expired refresh).
 */
export async function getAccessToken(): Promise<string | null> {
    if (!_msal || !_config) return null;
    const account = _msal.getActiveAccount() ?? _msal.getAllAccounts()[0] ?? null;
    if (!account) return null;

    try {
        const result = await _msal.acquireTokenSilent({
            account,
            scopes: [_config.apiScope],
        });
        return result.accessToken;
    } catch (err) {
        if (err instanceof InteractionRequiredAuthError) {
            await _msal.acquireTokenRedirect({ scopes: [_config.apiScope] });
            return null;
        }
        throw err;
    }
}
