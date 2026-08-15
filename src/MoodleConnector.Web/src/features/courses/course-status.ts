import type { Course } from './courses-gateway';

export type CourseLifecycle = 'in_progress' | 'not_started' | 'finished';
export type CourseLifecycleFilter = CourseLifecycle | 'all';

function parseDate(value?: string) {
  if (!value) return undefined;
  const timestamp = Date.parse(value);
  return Number.isNaN(timestamp) ? undefined : timestamp;
}

export function getCourseLifecycle(course: Pick<Course, 'startDate' | 'endDate'>, now = Date.now()): CourseLifecycle {
  const start = course.startDate ? new Date(course.startDate).getTime() : undefined;
  const end = course.endDate ? new Date(course.endDate).getTime() : undefined;
  if (end && end < now) return 'finished';
  if (start && start > now) return 'not_started';
  return 'in_progress';
}

export function filterCoursesByLifecycle<T extends Pick<Course, 'startDate' | 'endDate'>>(courses: T[], filter: CourseLifecycleFilter): T[] {
  return filter === 'all' ? courses : courses.filter((course) => getCourseLifecycle(course) === filter);
}

export function normalizeCourseEndDatesBySequence<T extends Pick<Course, 'courseId' | 'categoryName' | 'startDate' | 'endDate'>>(courses: T[]): T[] {
  const groups = new Map<string, T[]>();
  courses.forEach((course) => {
    const category = course.categoryName?.trim();
    if (!category) return;
    const key = category.toLocaleLowerCase('pt-BR');
    const group = groups.get(key) ?? [];
    group.push(course);
    groups.set(key, group);
  });

  const adjustedEndDates = new Map<T, string>();
  groups.forEach((group) => {
    if (group.length < 2) return;
    const endTimes = group.map((course) => parseDate(course.endDate));
    const commonEndTime = endTimes[0];
    if (commonEndTime === undefined || endTimes.some((time) => time !== commonEndTime)) return;

    const startsByTime = new Map<number, string>();
    group.forEach((course) => {
      const startTime = parseDate(course.startDate);
      if (startTime !== undefined && !startsByTime.has(startTime)) startsByTime.set(startTime, course.startDate!);
    });
    const starts = [...startsByTime.keys()].sort((left, right) => left - right);
    if (starts.length < 2) return;

    group.forEach((course) => {
      const startTime = parseDate(course.startDate);
      if (startTime === undefined) return;
      const nextStart = starts.find((candidate) => candidate > startTime);
      if (nextStart !== undefined) adjustedEndDates.set(course, startsByTime.get(nextStart)!);
    });
  });

  return courses.map((course) => {
    const endDate = adjustedEndDates.get(course);
    return endDate ? { ...course, endDate } : course;
  });
}
