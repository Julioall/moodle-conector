import { createAppClient } from '../../integrations/http-client';

export type Course = { connectionRef: string; courseId: string; shortName?: string; fullName: string; displayName?: string; categoryName?: string; startDate?: string; endDate?: string; visible?: boolean; viewUrl?: string; courseImage?: string; progress?: number; lastAccessAt?: string };
export type Activity = { connectionRef: string; courseId: string; activityId: string; activityType: string; name: string; url?: string; visible?: boolean; userVisible?: boolean; hasDates: boolean; hasDeadline: boolean; dueAt?: string; openAt?: string; closeAt?: string; fileCount: number };
export type ListResponse<T> = { data: T[]; meta: { page: number; pageSize: number; returned: number; total?: number | null; hasMore: boolean; generatedAt: string; connectionRef?: string } };
export type CourseResponse = { data: Course; meta: { generatedAt: string; connectionRef?: string } };

export const createCoursesGateway = (client = createAppClient()) => ({
  list: (connectionRef?: string, page = 1, pageSize = 20) => client.get<ListResponse<Course>>(`/api/courses?${new URLSearchParams({ ...(connectionRef ? { connectionRef } : {}), page: String(page), pageSize: String(pageSize) })}`),
  get: (connectionRef: string, courseId: string) => client.get<CourseResponse>(`/api/courses/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}`),
  activities: (connectionRef: string, courseId: string, page = 1, pageSize = 20) => client.get<ListResponse<Activity>>(`/api/courses/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}/activities?page=${page}&pageSize=${pageSize}`),
});
export const coursesGateway = createCoursesGateway();

