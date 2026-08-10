import { createPortalClient, type PortalClient } from '../../integrations/http/portal-client';
export type PendingType = 'no_recent_access' | 'not_submitted' | 'awaiting_grading' | 'low_grade' | 'upcoming_deadline';
export type PendingLevel = 'normal' | 'attention' | 'risk' | 'critical';
export type PendingItem = { connectionRef: string; courseId: string; studentId: string; activityId?: string; studentName: string; activityName: string; type: PendingType; level: PendingLevel; factors: string[]; dueAt?: string; grade?: number; lastAccessAt?: string; moodleUrl?: string; canGrade: false; canWrite: false };
export type PendingListResponse = { data: PendingItem[]; meta: { page: number; pageSize: number; returned: number; total?: number; hasMore: boolean; generatedAt: string; connectionRef?: string } };
export type PendingFilters = { connectionRef?: string; courseId?: string; studentId?: string; type?: PendingType; level?: PendingLevel; period?: string; page?: number; pageSize?: number };
export const createPendingGateway = (client: PortalClient = createPortalClient()) => ({ list: (filters: PendingFilters = {}) => { const { page = 1, pageSize = 20, ...rest } = filters; const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) }); Object.entries(rest).forEach(([key, value]) => { if (value) query.set(key, value); }); return client.get<PendingListResponse>(`/api/portal/pending?${query.toString()}`); } });
export const pendingGateway = createPendingGateway();
