import { describe, expect, it } from 'vitest';
import { filterCoursesByLifecycle, getCourseLifecycle, normalizeCourseEndDatesBySequence } from '../features/courses/course-status';

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

  it('uses the next distinct opening as the end of sequential units with a common module end', () => {
    const courses = [
      { courseId: 'a', categoryName: 'Turma 1', startDate: '2026-01-01T00:00:00Z', endDate: '2026-12-31T23:59:59Z' },
      { courseId: 'b', categoryName: 'Turma 1', startDate: '2026-01-01T00:00:00Z', endDate: '2026-12-31T23:59:59Z' },
      { courseId: 'c', categoryName: 'Turma 1', startDate: '2026-03-01T00:00:00Z', endDate: '2026-12-31T23:59:59Z' },
    ];

    const normalized = normalizeCourseEndDatesBySequence(courses);

    expect(normalized.map((course) => course.endDate)).toEqual([
      '2026-03-01T00:00:00Z',
      '2026-03-01T00:00:00Z',
      '2026-12-31T23:59:59Z',
    ]);
  });

  it('keeps the common end when all units open together', () => {
    const courses = [
      { courseId: 'a', categoryName: 'Turma 2', startDate: '2026-01-01T00:00:00Z', endDate: '2026-12-31T23:59:59Z' },
      { courseId: 'b', categoryName: 'Turma 2', startDate: '2026-01-01T00:00:00Z', endDate: '2026-12-31T23:59:59Z' },
    ];

    expect(normalizeCourseEndDatesBySequence(courses)).toEqual(courses);
  });
});
