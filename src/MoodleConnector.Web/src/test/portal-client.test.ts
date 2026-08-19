import { describe, expect, it, vi } from 'vitest'; import { createAppClient, AppHttpError } from '../integrations/http/api-client';
describe('app client', () => { it('uses same-origin credentials and parses JSON', async () => { const fetcher=vi.fn().mockResolvedValue(new Response('{"ok":true}',{status:200})); await expect(createAppClient(fetcher).get('/api/session')).resolves.toEqual({ok:true}); expect(fetcher).toHaveBeenCalledWith('/api/session',expect.objectContaining({credentials:'same-origin',method:'GET'})); }); it('normalizes HTTP failures', async () => { const fetcher=vi.fn().mockResolvedValue(new Response('',{status:401})); await expect(createAppClient(fetcher).get('/api/session')).rejects.toMatchObject({status:401} satisfies Partial<AppHttpError>); }); it('uses the CSRF token returned by the endpoint for mutations', async () => { const fetcher=vi.fn().mockImplementation((path: string) => path === '/api/csrf' ? Promise.resolve(new Response('{"token":"fresh-token"}', { status: 200 })) : Promise.resolve(new Response('{"ok":true}', { status: 200 }))); await expect(createAppClient(fetcher).request('/api/connections', { method: 'POST' })).resolves.toEqual({ ok: true }); const mutationCall = fetcher.mock.calls.at(-1); expect((mutationCall?.[1] as RequestInit).headers).toBeInstanceOf(Headers); expect(((mutationCall?.[1] as RequestInit).headers as Headers).get('X-CSRF-TOKEN')).toBe('fresh-token'); }); });

describe('app client mutation efficiency', () => {
  it('reuses the CSRF token across consecutive mutations', async () => {
    const fetcher = vi.fn().mockImplementation((path: string) => path === '/api/csrf'
      ? Promise.resolve(new Response('{"token":"fresh-token"}', { status: 200 }))
      : Promise.resolve(new Response('{"ok":true}', { status: 200 })));
    const client = createAppClient(fetcher);

    await client.request('/api/tasks/1', { method: 'DELETE' });
    await client.request('/api/tasks/2', { method: 'DELETE' });

    expect(fetcher.mock.calls.filter(([path]) => path === '/api/csrf')).toHaveLength(1);
    expect(fetcher.mock.calls.filter(([path]) => path === '/api/tasks/1')).toHaveLength(1);
    expect(fetcher.mock.calls.filter(([path]) => path === '/api/tasks/2')).toHaveLength(1);
  });
});

describe('app Wave B contracts', () => {
  it('preserves the envelope returned by the session endpoint', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response('{"data":{"authenticated":true},"meta":{"generatedAt":"2026-08-10T00:00:00Z"}}', { status: 200 }));
    await expect(createAppClient(fetcher).get('/api/session')).resolves.toMatchObject({ data: { authenticated: true }, meta: { generatedAt: expect.any(String) } });
  });
});


