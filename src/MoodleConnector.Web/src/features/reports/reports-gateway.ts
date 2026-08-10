import { createPortalClient, type PortalClient } from '../../integrations/http/portal-client';

export type OperationalReport = { data: { openTasks: number; completedTasks: number; upcomingEvents: number; followupsRecorded: number; generatedAt: string }; meta: { generatedAt: string } };
export type CourseOverviewReport = { data: { totalActiveStudents: number; studentsInactiveDays: number }; meta: { generatedAt: string } };
export type WeeklyReport = { data: { studentsWithAttention: number; studentsAtRisk: number }; meta: { generatedAt: string } };
export type CompletionReport = { data: { likelyComplete: number; pendingRecovery: number }; meta: { generatedAt: string } };
export type AuditReport = { data: { totalActions: number; completedActions: number; failedActions: number; confirmedActions: number }; meta: { generatedAt: string } };

export const createReportsGateway = (client: PortalClient = createPortalClient()) => ({
  operational: () => client.get<OperationalReport>('/api/portal/reports/operational'),
  audit: () => client.get<AuditReport>('/api/portal/reports/audit'),
  courseOverview: (connectionRef: string, courseId: string) => client.get<CourseOverviewReport>(`/api/portal/reports/course-overview/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}`),
  weekly: (connectionRef: string, courseId: string) => client.get<WeeklyReport>(`/api/portal/reports/weekly/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}`),
  completion: (connectionRef: string, courseId: string) => client.get<CompletionReport>(`/api/portal/reports/completion/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}`),
});

export const reportsGateway = createReportsGateway();
