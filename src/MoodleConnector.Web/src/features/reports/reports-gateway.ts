import { createAppClient, type AppClient } from '../../integrations/http-client';

export type OperationalReport = { data: { openTasks: number; completedTasks: number; upcomingEvents: number; followupsRecorded: number; generatedAt: string }; meta: { generatedAt: string } };
export type CourseOverviewReport = { data: { totalActiveStudents: number; studentsInactiveDays: number }; meta: { generatedAt: string } };
export type WeeklyReport = { data: { studentsWithAttention: number; studentsAtRisk: number }; meta: { generatedAt: string } };
export type CompletionReport = { data: { likelyComplete: number; pendingRecovery: number }; meta: { generatedAt: string } };
export type AuditReport = { data: { totalActions: number; completedActions: number; failedActions: number; confirmedActions: number }; meta: { generatedAt: string } };

export const createReportsGateway = (client: AppClient = createAppClient()) => ({
  operational: () => client.get<OperationalReport>('/api/reports/operational'),
  audit: () => client.get<AuditReport>('/api/reports/audit'),
  courseOverview: (connectionRef: string, courseId: string) => client.get<CourseOverviewReport>(`/api/reports/course-overview/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}`),
  weekly: (connectionRef: string, courseId: string) => client.get<WeeklyReport>(`/api/reports/weekly/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}`),
  completion: (connectionRef: string, courseId: string) => client.get<CompletionReport>(`/api/reports/completion/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}`),
});

export const reportsGateway = createReportsGateway();

