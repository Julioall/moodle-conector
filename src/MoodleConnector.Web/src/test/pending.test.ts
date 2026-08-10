import { describe, expect, it, vi } from 'vitest';
import { createPendingGateway, type PendingListResponse } from '../features/pending/pending-gateway';

describe('pending gateway', () => {
  it('encodes deterministic filters and keeps the read-only endpoint', async () => {
    const client = { get: vi.fn().mockResolvedValue({ data: [], meta: { page: 2, pageSize: 20, returned: 0, hasMore: false, generatedAt: '2026-08-10T00:00:00Z' } } satisfies PendingListResponse), request: vi.fn() };
    await createPendingGateway(client).list({ connectionRef: 'senai goias', courseId: '42', studentId: '7', type: 'awaiting_grading', level: 'risk', period: '7d', page: 2 });
    expect(client.get).toHaveBeenCalledWith(expect.stringContaining('/api/portal/pending?'));
    expect(client.get.mock.calls[0][0]).toContain('connectionRef=senai+goias');
    expect(client.get.mock.calls[0][0]).toContain('type=awaiting_grading');
    expect(client.get.mock.calls[0][0]).not.toMatch(/(POST|grade|confirm|write)/i);
  });
});
