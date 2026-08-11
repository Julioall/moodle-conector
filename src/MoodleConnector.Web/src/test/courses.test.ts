import { afterEach, describe, expect, it, vi } from 'vitest';
import { createCoursesGateway } from '../features/courses/courses-gateway';

describe('courses gateway', () => {
  afterEach(() => vi.unstubAllGlobals());
  it('uses composite connectionRef in course and activities URLs', async () => {
    const get = vi.fn().mockResolvedValue({ data: [], meta: { page: 1, pageSize: 20, returned: 0, hasMore: false, generatedAt: '2026-08-10T00:00:00Z' }});
    await createCoursesGateway({ get } as never).activities('senai-go', '42');
    expect(get).toHaveBeenCalledWith('/api/courses/senai-go/42/activities?page=1&pageSize=20');
  });
});

