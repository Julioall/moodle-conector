export type AppErrorBody = { error?: { code?: string; message?: string }; message?: string };

export class AppHttpError extends Error {
  constructor(public readonly status: number, message: string, public readonly correlationId?: string, public readonly body?: AppErrorBody) {
    super(message); this.name = 'AppHttpError';
  }
}

export type AppClient = { get<T>(path: string, options?: RequestInit): Promise<T>; request<T>(path: string, options?: RequestInit): Promise<T> };

const mutatingMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
const createCorrelationId = () => globalThis.crypto?.randomUUID?.() ?? `app-${Date.now()}-${Math.random().toString(16).slice(2)}`;

export function readCsrfToken(): string | undefined {
  const meta = typeof document !== 'undefined' ? document.querySelector<HTMLMetaElement>('meta[name="csrf-token"]')?.content : undefined;
  if (meta) return meta;
  const cookie = typeof document !== 'undefined' ? document.cookie.split('; ').find((item) => item.startsWith('XSRF-TOKEN=')) : undefined;
  return cookie ? decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) : undefined;
}

export function createAppClient(fetchImpl: typeof fetch = fetch, timeoutMs = 10000): AppClient {
  return {
    get: <T>(path: string, options?: RequestInit) => request<T>(path, { ...options, method: 'GET' }),
    request: <T>(path: string, options: RequestInit = {}) => request<T>(path, options),
  };

  async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
    const controller = new AbortController();
    const externalSignal = options.signal;
    const abortExternal = () => controller.abort(externalSignal?.reason);
    externalSignal?.addEventListener('abort', abortExternal, { once: true });
    const timer = setTimeout(() => controller.abort('timeout'), timeoutMs);
    const method = (options.method ?? 'GET').toUpperCase();
    const correlationId = createCorrelationId();
    const headers = new Headers(options.headers);
    headers.set('Accept', 'application/json');
    headers.set('X-Correlation-ID', correlationId);
    if (mutatingMethods.has(method)) {
      // Always issue a fresh token before a mutation. A token left in the
      // browser cookie can be stale after a deploy, restart or long-lived tab.
      // The endpoint also returns the token in JSON, which is more reliable
      // than depending only on Set-Cookie being observed by the browser.
      const csrfResponse = await fetchImpl('/api/csrf', { method: 'GET', credentials: 'same-origin', cache: 'no-store' });
      const csrfBody = await csrfResponse.json() as { token?: string };
      const csrf = csrfBody.token ?? readCsrfToken();
      if (!csrf) throw new AppHttpError(400, 'CSRF token is required for this operation', correlationId, { error: { code: 'csrf_missing' } });
      headers.set('X-CSRF-TOKEN', csrf);
    }
    try {
      const response = await fetchImpl(path, { ...options, method, credentials: 'same-origin', signal: controller.signal, headers });
      const responseCorrelationId = response.headers.get('X-Correlation-ID') ?? correlationId;
      const text = await response.text();
      let body: AppErrorBody | undefined;
      if (text) { try { body = JSON.parse(text) as AppErrorBody; } catch { body = undefined; } }
      if (!response.ok) throw new AppHttpError(response.status, body?.error?.message ?? body?.message ?? `App request failed (${response.status})`, responseCorrelationId, body);
      return (body ?? {}) as T;
    } catch (error) {
      if (error instanceof AppHttpError) throw error;
      if (error instanceof DOMException && error.name === 'AbortError' || controller.signal.aborted) {
        if (externalSignal?.aborted) throw new AppHttpError(499, 'App request was cancelled', correlationId);
        throw new AppHttpError(408, 'App request timed out', correlationId);
      }
      throw error;
    } finally { clearTimeout(timer); externalSignal?.removeEventListener('abort', abortExternal); }
  }
}

