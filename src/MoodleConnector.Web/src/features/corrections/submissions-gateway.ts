import { createAppClient } from '../../integrations/http/api-client';

export type SubmissionFile = { filename: string; mimeType?: string; sizeBytes?: number; fileUrl: string };
export type Submission = {
  userId: string;
  fullName?: string;
  submissionId?: string;
  status: string;
  gradingStatus?: string;
  submitted: boolean;
  late: boolean;
  needsGrading: boolean;
  submittedAt?: string;
  modifiedAt?: string;
  attemptNumber?: number;
  fileCount: number;
  hasOnlineText: boolean;
  files: SubmissionFile[];
  currentGrade?: number;
  currentFeedback?: string;
  gradeMax?: number;
};

export type SubmissionPage = {
  courseId: string;
  assignmentId: string;
  assignmentModuleId?: string;
  assignmentName: string;
  page: number;
  pageSize: number;
  filter: string;
  includeLate: boolean;
  includeUngraded: boolean;
  since?: string;
  before?: string;
  total: number;
  hasMore: boolean;
  submissions: Submission[];
};

export type IndividualGradePreview = {
  assignmentId: string;
  studentId: string;
  studentFullName: string;
  courseId: string;
  proposedGrade: number;
  gradeMax?: number;
  previousGrade?: number;
  previousFeedback?: string;
  confirmationText: string;
  risks: string[];
  expiresAt: string;
};

export type GradePrepareResult = { pendingActionId: string; status: string; preview: IndividualGradePreview };
export type GradeSendResult = { status: string; pendingActionId: string; assignmentId: string; studentId: string; launchedGrade: number; auditId?: string; warnings: string[] };
type SnapshotMeta = { generatedAt: string; connectionRef?: string; source?: string; snapshotAt?: string; ageSeconds?: number; stale?: boolean; refreshQueued?: boolean; complete?: boolean };
type Envelope<T> = { data: T; meta: SnapshotMeta };
type ListEnvelope<T> = { data: T[]; meta: SnapshotMeta & { page: number; pageSize: number; returned: number; hasMore: boolean; warnings?: string[]; total?: number } };

export const createSubmissionsGateway = (client = createAppClient()) => ({
  list: (connectionRef: string, courseId: string, assignmentId: string, status = 'awaiting_grading', page = 1, pageSize = 25, refresh = false) => {
    const query = new URLSearchParams({ connectionRef, courseId, assignmentId, status, page: String(page), pageSize: String(pageSize), includeLate: 'true', includeUngraded: 'true', ...(refresh ? { refresh: 'true' } : {}) });
    return client.get<Envelope<SubmissionPage>>(`/api/submissions?${query}`);
  },
  detail: (connectionRef: string, courseId: string, assignmentId: string, studentId: string) =>
    client.get<Envelope<Submission>>(`/api/submissions/${encodeURIComponent(courseId)}/${encodeURIComponent(assignmentId)}/${encodeURIComponent(studentId)}?connectionRef=${encodeURIComponent(connectionRef)}`),
  prepareGrade: (input: { connectionRef: string; courseId: string; assignmentId: string; studentId: string; proposedGrade: number; feedbackText?: string; justificationText: string }) =>
    client.request<Envelope<GradePrepareResult>>('/api/grading/individual/prepare', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }),
  confirmGrade: (input: { connectionRef: string; pendingActionId: string; confirmationText: string }) =>
    client.request<Envelope<GradeSendResult>>('/api/grading/individual/confirm', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }),
});

export const submissionsGateway = createSubmissionsGateway();

export type PendingItem = {
  connectionRef: string;
  courseId: string;
  studentId: string;
  activityId?: string;
  studentName: string;
  activityName: string;
  type: string;
  level: string;
  factors: string[];
  dueAt?: string;
  grade?: number;
  lastAccessAt?: string;
  moodleUrl?: string;
  canGrade: boolean;
  canWrite: boolean;
};

export const createPendingGateway = (client = createAppClient()) => ({
  list: (connectionRef: string, courseId: string, refresh = false) => client.get<ListEnvelope<PendingItem>>(`/api/pending?${new URLSearchParams({ connectionRef, courseId, page: '1', pageSize: '100', ...(refresh ? { refresh: 'true' } : {}) })}`),
});

export const pendingGateway = createPendingGateway();

export type Evidence = { id: string; connectionRef?: string; courseId: string; studentId?: string; activityId?: string; kind: string; title: string; details: string; source: string; observedAt: string; createdAt: string };

export const createEvidenceGateway = (client = createAppClient()) => ({
  list: (connectionRef: string, courseId: string) => client.get<{ data: Evidence[]; meta: { page: number; pageSize: number; returned: number; total?: number; hasMore: boolean; generatedAt: string } }>(`/api/evidence?${new URLSearchParams({ connectionRef, courseId, page: '1', pageSize: '30' })}`),
});

export const evidenceGateway = createEvidenceGateway();
