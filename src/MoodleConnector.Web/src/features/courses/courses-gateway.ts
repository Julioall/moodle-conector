import { createAppClient } from '../../integrations/http/api-client';

export type Course = { connectionRef: string; courseId: string; shortName?: string; fullName: string; displayName?: string; categoryName?: string; startDate?: string; endDate?: string; visible?: boolean; viewUrl?: string; courseImage?: string; progress?: number; lastAccessAt?: string };
export type Activity = { connectionRef: string; courseId: string; activityId: string; instanceId?: string; activityType: string; name: string; url?: string; visible?: boolean; userVisible?: boolean; hasDates: boolean; hasDeadline: boolean; dueAt?: string; openAt?: string; closeAt?: string; fileCount: number; pendingSubmissionCount?: number; awaitingGradingCount?: number };
export type ListResponse<T> = { data: T[]; meta: { page: number; pageSize: number; returned: number; total?: number | null; hasMore: boolean; generatedAt: string; connectionRef?: string; warnings?: string[]; source?: string; snapshotAt?: string; ageSeconds?: number; stale?: boolean; refreshQueued?: boolean; complete?: boolean } };
export type CourseResponse = { data: Course; meta: { generatedAt: string; connectionRef?: string; source?: string; snapshotAt?: string; ageSeconds?: number; stale?: boolean; refreshQueued?: boolean; complete?: boolean } };
export type CourseHierarchyNode = { path: string; name: string; level: number; courseCount: number };

export const createCoursesGateway = (client = createAppClient()) => {
  const list = (connectionRef?: string, page = 1, pageSize = 20) => client.get<ListResponse<Course>>(`/api/courses?${new URLSearchParams({ ...(connectionRef ? { connectionRef } : {}), page: String(page), pageSize: String(pageSize) })}`);
  const byCategory = (categoryPath: string, connectionRef?: string, page = 1, pageSize = 50) => client.get<ListResponse<Course>>(`/api/schools/courses?${new URLSearchParams({ categoryPath, ...(connectionRef ? { connectionRef } : {}), page: String(page), pageSize: String(pageSize) })}`);

  const listAll = async (connectionRef?: string, pageSize = 100) => {
    const firstPage = await list(connectionRef, 1, pageSize);
    const courses = [...firstPage.data];
    let currentPage = firstPage;
    let page = 2;

    while ((currentPage.meta.hasMore || currentPage.data.length === pageSize) && currentPage.data.length > 0) {
      currentPage = await list(connectionRef, page, pageSize);
      if (currentPage.data.length === 0) break;
      courses.push(...currentPage.data);
      page += 1;
    }

    return {
      data: courses,
      meta: { ...firstPage.meta, returned: courses.length, hasMore: false },
    } satisfies ListResponse<Course>;
  };

  const listAllByCategory = async (categoryPath: string, connectionRef?: string, pageSize = 100) => {
    const firstPage = await byCategory(categoryPath, connectionRef, 1, pageSize);
    const courses = [...firstPage.data];
    let currentPage = firstPage;
    let page = 2;

    while ((currentPage.meta.hasMore || currentPage.data.length === pageSize) && currentPage.data.length > 0) {
      currentPage = await byCategory(categoryPath, connectionRef, page, pageSize);
      if (currentPage.data.length === 0) break;
      courses.push(...currentPage.data);
      page += 1;
    }

    return {
      data: courses,
      meta: { ...firstPage.meta, returned: courses.length, hasMore: false },
    } satisfies ListResponse<Course>;
  };

  return {
  hierarchy: (connectionRef?: string) => client.get<{ data: CourseHierarchyNode[]; meta: { generatedAt: string; connectionRef?: string } }>(`/api/schools?${new URLSearchParams(connectionRef ? { connectionRef } : {})}`),
  byCategory,
  listAllByCategory,
  list,
  listAll,
  get: (connectionRef: string, courseId: string, refresh = false) => client.get<CourseResponse>(`/api/courses/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}${refresh ? '?refresh=true' : ''}`),
  activities: (connectionRef: string, courseId: string, page = 1, pageSize = 20, includeActionSummary = false, refresh = false) => client.get<ListResponse<Activity>>(`/api/courses/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}/activities?page=${page}&pageSize=${pageSize}${includeActionSummary ? '&includeActionSummary=true' : ''}${refresh ? '&refresh=true' : ''}`),
  };
};
export const coursesGateway = createCoursesGateway();


