export type PortalErrorBody = { error?: { code?: string; message?: string }; message?: string };

export class PortalHttpError extends Error {
  constructor(public readonly status: number, message: string, public readonly correlationId?: string, public readonly body?: PortalErrorBody) {
    super(message); this.name = 'PortalHttpError';
  }
}

export type PortalClient = { get<T>(path: string, options?: RequestInit): Promise<T>; request<T>(path: string, options?: RequestInit): Promise<T> };

const mutatingMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
const createCorrelationId = () => globalThis.crypto?.randomUUID?.() ?? `portal-${Date.now()}-${Math.random().toString(16).slice(2)}`;

export function readCsrfToken(): string | undefined {
  const meta = typeof document !== 'undefined' ? document.querySelector<HTMLMetaElement>('meta[name="csrf-token"]')?.content : undefined;
  if (meta) return meta;
  const cookie = typeof document !== 'undefined' ? document.cookie.split('; ').find((item) => item.startsWith('XSRF-TOKEN=')) : undefined;
  return cookie ? decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) : undefined;
}

export function createPortalClient(fetchImpl: typeof fetch = fetch, timeoutMs = 10000): PortalClient {
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
      let csrf = readCsrfToken();
      if (!csrf) {
        await fetchImpl('/api/portal/csrf', { method: 'GET', credentials: 'same-origin' });
        csrf = readCsrfToken();
      }
      if (!csrf) throw new PortalHttpError(400, 'CSRF token is required for this operation', correlationId, { error: { code: 'csrf_missing' } });
      headers.set('X-CSRF-TOKEN', csrf);
    }
    try {
      const response = await fetchImpl(path, { ...options, method, credentials: 'same-origin', signal: controller.signal, headers });
      const responseCorrelationId = response.headers.get('X-Correlation-ID') ?? correlationId;
      const text = await response.text();
      let body: PortalErrorBody | undefined;
      if (text) { try { body = JSON.parse(text) as PortalErrorBody; } catch { body = undefined; } }
      if (!response.ok) throw new PortalHttpError(response.status, body?.error?.message ?? body?.message ?? `Portal request failed (${response.status})`, responseCorrelationId, body);
      return (body ?? {}) as T;
    } catch (error) {
      if (error instanceof PortalHttpError) throw error;
      if (error instanceof DOMException && error.name === 'AbortError' || controller.signal.aborted) {
        if (externalSignal?.aborted) throw new PortalHttpError(499, 'Portal request was cancelled', correlationId);
        throw new PortalHttpError(408, 'Portal request timed out', correlationId);
      }
      throw error;
    } finally { clearTimeout(timer); externalSignal?.removeEventListener('abort', abortExternal); }
  }
}
