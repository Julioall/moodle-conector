import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { useIgnoredCourses } from '../features/courses/course-visibility';

afterEach(() => window.localStorage.clear());

describe('course visibility preferences', () => {
  it('persists ignored courses per Moodle connection and restores them', () => {
    const { result } = renderHook(() => useIgnoredCourses('fieg'));

    act(() => result.current.ignoreCourse('30585'));
    expect(result.current.ignoredCourseIds.has('30585')).toBe(true);

    act(() => result.current.ignoreCourses(['30586', '30587']));
    expect([...result.current.ignoredCourseIds]).toEqual(['30585', '30586', '30587']);

    const secondHook = renderHook(() => useIgnoredCourses('fieg'));
    expect(secondHook.result.current.ignoredCourseIds.has('30585')).toBe(true);

    act(() => secondHook.result.current.restoreCourse('30585'));
    expect(secondHook.result.current.ignoredCourseIds.has('30585')).toBe(false);
    const restoredHook = renderHook(() => useIgnoredCourses('fieg'));
    expect(restoredHook.result.current.ignoredCourseIds.has('30585')).toBe(false);
  });

  it('does not share ignored courses between connections', () => {
    const { result: fieg } = renderHook(() => useIgnoredCourses('fieg'));
    const { result: nacional } = renderHook(() => useIgnoredCourses('nacional'));

    act(() => fieg.current.ignoreCourse('30585'));

    expect(fieg.current.ignoredCourseIds.has('30585')).toBe(true);
    expect(nacional.current.ignoredCourseIds.has('30585')).toBe(false);
  });
});
