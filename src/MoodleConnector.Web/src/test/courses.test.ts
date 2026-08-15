import { afterEach, describe, expect, it, vi } from 'vitest';
import { createCoursesGateway } from '../features/courses/courses-gateway';

describe('courses gateway', () => {
  afterEach(() => vi.unstubAllGlobals());
  it('uses composite connectionRef in course and activities URLs', async () => {
    const get = vi.fn().mockResolvedValue({ data: [], meta: { page: 1, pageSize: 20, returned: 0, hasMore: false, generatedAt: '2026-08-10T00:00:00Z' }});
    await createCoursesGateway({ get } as never).activities('senai-go', '42');
    expect(get).toHaveBeenCalledWith('/api/courses/senai-go/42/activities?page=1&pageSize=20');
  });

  it('loads all course pages for the catalog', async () => {
    const get = vi.fn()
      .mockResolvedValueOnce({ data: [{ courseId: '1' }, { courseId: '2' }], meta: { page: 1, pageSize: 2, returned: 2, total: 3, hasMore: true, generatedAt: '2026-08-10T00:00:00Z' } })
      .mockResolvedValueOnce({ data: [{ courseId: '3' }], meta: { page: 2, pageSize: 2, returned: 1, total: 3, hasMore: false, generatedAt: '2026-08-10T00:00:01Z' } });

    const response = await createCoursesGateway({ get } as never).listAll('fieg', 2);

    expect(response.data.map((course) => course.courseId)).toEqual(['1', '2', '3']);
    expect(response.meta.returned).toBe(3);
    expect(get).toHaveBeenNthCalledWith(1, '/api/courses?connectionRef=fieg&page=1&pageSize=2');
    expect(get).toHaveBeenNthCalledWith(2, '/api/courses?connectionRef=fieg&page=2&pageSize=2');
  });

  it('continues when a full page has no hasMore flag', async () => {
    const get = vi.fn()
      .mockResolvedValueOnce({ data: [{ courseId: '1' }, { courseId: '2' }], meta: { page: 1, pageSize: 2, returned: 2, total: 3, hasMore: false, generatedAt: '2026-08-10T00:00:00Z' } })
      .mockResolvedValueOnce({ data: [{ courseId: '3' }], meta: { page: 2, pageSize: 2, returned: 1, total: 3, hasMore: false, generatedAt: '2026-08-10T00:00:01Z' } });

    const response = await createCoursesGateway({ get } as never).listAll('fieg', 2);

    expect(response.data.map((course) => course.courseId)).toEqual(['1', '2', '3']);
    expect(get).toHaveBeenNthCalledWith(2, '/api/courses?connectionRef=fieg&page=2&pageSize=2');
  });
});

