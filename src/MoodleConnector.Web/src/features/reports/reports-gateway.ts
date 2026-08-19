import { createAppClient, type AppClient } from '../../integrations/http/api-client';

export type ReportType = 'grades' | 'weekly' | 'overview' | 'completion';

export type ReportCourse = {
  name: string;
  categoryName?: string;
};

export type ReportJob = {
  id: string;
  reportType: ReportType;
  scopeType: 'category' | 'course' | 'courses';
  connectionRef: string;
  categoryPath?: string;
  courseId?: string;
  courseIds?: string[];
  status: 'queued' | 'running' | 'completed' | 'failed';
  progressPercent: number;
  totalCourses: number;
  processedCourses: number;
  fileName?: string;
  contentType?: string;
  fileSizeBytes: number;
  errorMessage?: string;
  requestedAt: string;
  startedAt?: string;
  completedAt?: string;
  updatedAt: string;
  downloadUrl?: string;
  courses?: ReportCourse[];
};

export type ReportJobsResponse = {
  data: ReportJob[];
  meta: {
    page: number;
    pageSize: number;
    returned: number;
    total?: number | null;
    hasMore: boolean;
    generatedAt: string;
    storageUsedBytes: number;
    storageLimitBytes: number;
    storageAvailableBytes: number;
  };
};

export type CreateReportJobInput = {
  reportType: 'grades';
  scopeType: ReportJob['scopeType'];
  connectionRef: string;
  categoryPath?: string;
  courseId?: string;
  courseIds?: string[];
};

export const createReportsGateway = (client: AppClient = createAppClient()) => ({
  jobs: (page = 1, pageSize = 20) => client.get<ReportJobsResponse>(`/api/reports/jobs?page=${page}&pageSize=${pageSize}`),
  deleteJob: (id: string) => client.request<void>(`/api/reports/jobs/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  createJob: async (input: CreateReportJobInput) => client.request<ReportJob>('/api/reports/jobs', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  }),
});

export const reportsGateway = createReportsGateway();
