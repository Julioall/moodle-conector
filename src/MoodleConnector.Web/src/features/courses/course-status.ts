import type { Course } from './courses-gateway';

export type CourseLifecycle = 'in_progress' | 'not_started' | 'finished';

export function getCourseLifecycle(course: Course, now = Date.now()): CourseLifecycle {
  const start = course.startDate ? new Date(course.startDate).getTime() : undefined;
  const end = course.endDate ? new Date(course.endDate).getTime() : undefined;
  if (end && end < now) return 'finished';
  if (start && start > now) return 'not_started';
  return 'in_progress';
}
