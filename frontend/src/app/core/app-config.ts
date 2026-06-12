/**
 * Resolves the SignalR hub URL.
 *
 * In production the SPA is served from the same origin as the API, so a relative path works.
 * During development (`ng serve` on :4200) the API runs separately on :5210.
 * See #12.
 */
export function resolveHubUrl(): string {
  return `${resolveApiBase()}/hubs/poker`;
}

/** Optional runtime config injected by public/config.js (editable per-deploy, no rebuild). */
interface PpRuntimeConfig {
  apiBase?: string;
}
declare global {
  interface Window {
    __PP_CONFIG__?: PpRuntimeConfig;
  }
}

/**
 * Base origin for REST + SignalR calls.
 *
 * Precedence:
 *  1. A non-empty `apiBase` in `config.js` — used for a SPLIT deploy (SPA on blob/S3/CDN, API on a
 *     separate server). Lets the same static bundle target any API origin without rebuilding.
 *  2. Otherwise inferred: the `ng serve` dev server (:4200) talks to the API on :5210; any other
 *     origin (the bundled single-artifact deploy) uses the same origin.
 */
export function resolveApiBase(): string {
  const configured = window.__PP_CONFIG__?.apiBase?.trim();
  if (configured) {
    return configured.replace(/\/+$/, ''); // tolerate a trailing slash in the configured value
  }

  const { protocol, hostname, port, origin } = window.location;
  const isAngularDevServer = port === '4200';
  return isAngularDevServer ? `${protocol}//${hostname}:5210` : origin;
}
