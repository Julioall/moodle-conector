import { useEffect, useState } from 'react';

const IGNORED_COURSES_KEY = 'app:ignored-courses';
type StoredIgnoredCourses = Record<string, string[]>;

function readStoredIgnoredCourses(): StoredIgnoredCourses {
  if (typeof window === 'undefined') return {};
  try {
    const value = JSON.parse(window.localStorage.getItem(IGNORED_COURSES_KEY) ?? '{}') as unknown;
    if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
    return Object.fromEntries(Object.entries(value).filter(([, ids]) => Array.isArray(ids) && ids.every((id) => typeof id === 'string')));
  } catch {
    return {};
  }
}

function readIgnoredCourseIds(connectionRef?: string) {
  if (!connectionRef) return new Set<string>();
  return new Set(readStoredIgnoredCourses()[connectionRef] ?? []);
}

function writeIgnoredCourseIds(connectionRef: string, ids: Set<string>) {
  if (typeof window === 'undefined') return;
  const stored = readStoredIgnoredCourses();
  if (ids.size === 0) delete stored[connectionRef];
  else stored[connectionRef] = [...ids];
  window.localStorage.setItem(IGNORED_COURSES_KEY, JSON.stringify(stored));
}

export function useIgnoredCourses(connectionRef?: string) {
  const [ignoredCourseIds, setIgnoredCourseIds] = useState(() => readIgnoredCourseIds(connectionRef));

  useEffect(() => {
    setIgnoredCourseIds(readIgnoredCourseIds(connectionRef));
  }, [connectionRef]);

  function updateIgnoredCourse(courseId: string, ignored: boolean) {
    if (!connectionRef) return;
    updateIgnoredCourses([courseId], ignored);
  }

  function updateIgnoredCourses(courseIds: string[], ignored: boolean) {
    if (!connectionRef || courseIds.length === 0) return;
    setIgnoredCourseIds((current) => {
      const next = new Set(current);
      courseIds.forEach((courseId) => {
        if (ignored) next.add(courseId);
        else next.delete(courseId);
      });
      writeIgnoredCourseIds(connectionRef, next);
      return next;
    });
  }

  return {
    ignoredCourseIds,
    ignoreCourse: (courseId: string) => updateIgnoredCourse(courseId, true),
    ignoreCourses: (courseIds: string[]) => updateIgnoredCourses(courseIds, true),
    restoreCourse: (courseId: string) => updateIgnoredCourse(courseId, false),
  };
}
