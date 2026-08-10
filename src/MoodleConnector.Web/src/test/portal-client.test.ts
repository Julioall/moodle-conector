import { describe, expect, it, vi } from 'vitest'; import { createPortalClient, PortalHttpError } from '../integrations/http/portal-client';
describe('portal client', () => { it('uses same-origin credentials and parses JSON', async () => { const fetcher=vi.fn().mockResolvedValue(new Response('{"ok":true}',{status:200})); await expect(createPortalClient(fetcher).get('/api/portal/session')).resolves.toEqual({ok:true}); expect(fetcher).toHaveBeenCalledWith('/api/portal/session',expect.objectContaining({credentials:'same-origin',method:'GET'})); }); it('normalizes HTTP failures', async () => { const fetcher=vi.fn().mockResolvedValue(new Response('',{status:401})); await expect(createPortalClient(fetcher).get('/api/portal/session')).rejects.toMatchObject({status:401} satisfies Partial<PortalHttpError>); }); });

describe('portal Wave B contracts', () => {
  it('preserves the envelope returned by the session endpoint', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response('{"data":{"authenticated":true},"meta":{"generatedAt":"2026-08-10T00:00:00Z"}}', { status: 200 }));
    await expect(createPortalClient(fetcher).get('/api/portal/session')).resolves.toMatchObject({ data: { authenticated: true }, meta: { generatedAt: expect.any(String) } });
  });
});
