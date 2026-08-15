import { describe, expect, it } from 'vitest';
import { filterCoursesByLifecycle, getCourseLifecycle } from '../features/courses/course-status';

const now = new Date('2026-08-14T12:00:00Z').getTime();

describe('course lifecycle', () => {
  it('classifies courses by their start and end dates', () => {
    expect(getCourseLifecycle({ startDate: '2026-08-01T00:00:00Z', endDate: '2026-08-31T23:59:59Z' } as never, now)).toBe('in_progress');
    expect(getCourseLifecycle({ startDate: '2026-08-15T00:00:00Z', endDate: '2026-08-31T23:59:59Z' } as never, now)).toBe('not_started');
    expect(getCourseLifecycle({ startDate: '2026-07-01T00:00:00Z', endDate: '2026-08-13T23:59:59Z' } as never, now)).toBe('finished');
  });

  it('filters courses without changing the all-courses view', () => {
    const courses = [
      { id: 'active', startDate: '2020-08-01T00:00:00Z', endDate: '2099-08-31T23:59:59Z' },
      { id: 'future', startDate: '2099-08-15T00:00:00Z', endDate: '2099-08-31T23:59:59Z' },
      { id: 'finished', startDate: '2020-07-01T00:00:00Z', endDate: '2021-08-13T23:59:59Z' },
    ];

    expect(filterCoursesByLifecycle(courses, 'in_progress').map((course) => course.id)).toEqual(['active']);
    expect(filterCoursesByLifecycle(courses, 'all')).toBe(courses);
  });
});
