import { describe, expect, it } from 'vitest';
import { getCourseLifecycle } from '../features/courses/course-status';

const now = new Date('2026-08-14T12:00:00Z').getTime();

describe('course lifecycle', () => {
  it('classifies courses by their start and end dates', () => {
    expect(getCourseLifecycle({ startDate: '2026-08-01T00:00:00Z', endDate: '2026-08-31T23:59:59Z' } as never, now)).toBe('in_progress');
    expect(getCourseLifecycle({ startDate: '2026-08-15T00:00:00Z', endDate: '2026-08-31T23:59:59Z' } as never, now)).toBe('not_started');
    expect(getCourseLifecycle({ startDate: '2026-07-01T00:00:00Z', endDate: '2026-08-13T23:59:59Z' } as never, now)).toBe('finished');
  });
});
