import { createAppClient, type AppClient } from '../../integrations/http/api-client';
export type DashboardPriority = { key: string; title: string; detail: string; level: string; courseId?: string; studentId?: string };
export type DashboardActivity = { key: string; title: string; detail: string; occurredAt?: string; courseId?: string; studentId?: string };
export type DashboardResponse = { data: { summary: { activeCourses: number; pendingDeliveries: number; awaitingGrading: number; studentsAtRisk: number; studentsNeedingAttention: number }; priorities: DashboardPriority[]; activitiesToReview: DashboardPriority[]; recentActivity: DashboardActivity[]; connectionRef?: string; warnings: string[] }; meta: { generatedAt: string; connectionRef?: string } };
export const createDashboardGateway = (client: AppClient = createAppClient()) => ({ get: (connectionRef?: string, courseId?: string) => { const query = new URLSearchParams(); if (connectionRef) query.set('connectionRef', connectionRef); if (courseId) query.set('courseId', courseId); const suffix = query.toString() ? `?${query}` : ''; return client.get<DashboardResponse>(`/api/dashboard${suffix}`); } });
export const dashboardGateway = createDashboardGateway();


