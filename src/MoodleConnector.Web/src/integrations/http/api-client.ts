export type AppErrorBody = { error?: { code?: string; message?: string }; message?: string };

export class AppHttpError extends Error {
  constructor(public readonly status: number, message: string, public readonly correlationId?: string, public readonly body?: AppErrorBody) {
    super(message); this.name = 'AppHttpError';
  }
}

export type AppClient = { get<T>(path: string, options?: RequestInit): Promise<T>; request<T>(path: string, options?: RequestInit): Promise<T> };

function repairMojibake(value: unknown): unknown {
  if (typeof value === 'string' && /[ÃÂâ][\u0080-\u00bfÃÂâ]/.test(value)) {
    try {
      const bytes = Uint8Array.from(value, (character) => character.charCodeAt(0));
      return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
    } catch { return value; }
  }
  if (Array.isArray(value)) return value.map(repairMojibake);
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, repairMojibake(item)]));
  return value;
}

const mutatingMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
const createCorrelationId = () => globalThis.crypto?.randomUUID?.() ?? `app-${Date.now()}-${Math.random().toString(16).slice(2)}`;

export function readCsrfToken(): string | undefined {
  const meta = typeof document !== 'undefined' ? document.querySelector<HTMLMetaElement>('meta[name="csrf-token"]')?.content : undefined;
  if (meta) return meta;
  const cookie = typeof document !== 'undefined' ? document.cookie.split('; ').find((item) => item.startsWith('XSRF-TOKEN=')) : undefined;
  return cookie ? decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) : undefined;
}

// Moodle reads can legitimately take longer than a browser-only API call,
// especially when a course catalogue is being assembled from remote data.
// Keep this aligned with MoodleApi:HttpTimeoutSeconds (30s by default).
export function createAppClient(fetchImpl: typeof fetch = fetch, timeoutMs = 30000): AppClient {
  let csrfToken: string | undefined;
  let csrfRequest: Promise<string> | undefined;

  async function getCsrfToken() {
    if (csrfToken) return csrfToken;
    if (!csrfRequest) {
      csrfRequest = (async () => {
        const csrfResponse = await fetchImpl('/api/csrf', { method: 'GET', credentials: 'same-origin', cache: 'no-store' });
        const csrfBody = await csrfResponse.json() as { token?: string };
        const token = csrfBody.token ?? readCsrfToken();
        if (!csrfResponse.ok || !token) throw new AppHttpError(csrfResponse.status || 400, 'CSRF token is required for this operation');
        csrfToken = token;
        return token;
      })().finally(() => { csrfRequest = undefined; });
    }
    return csrfRequest;
  }

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
      try { headers.set('X-CSRF-TOKEN', await getCsrfToken()); }
      catch (error) {
        if (error instanceof AppHttpError) throw new AppHttpError(error.status, error.message, correlationId, error.body);
        throw error;
      }
    }
    try {
      const response = await fetchImpl(path, { ...options, method, credentials: 'same-origin', signal: controller.signal, headers });
      const responseCorrelationId = response.headers.get('X-Correlation-ID') ?? correlationId;
      const text = await response.text();
      let body: AppErrorBody | undefined;
      if (text) { try { body = repairMojibake(JSON.parse(text)) as AppErrorBody; } catch { body = undefined; } }
      const errorMessage = typeof (body as { error?: unknown } | undefined)?.error === 'string'
        ? (body as { error: string }).error
        : body?.error?.message;
      if (!response.ok) throw new AppHttpError(response.status, errorMessage ?? body?.message ?? `App request failed (${response.status})`, responseCorrelationId, body);
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

