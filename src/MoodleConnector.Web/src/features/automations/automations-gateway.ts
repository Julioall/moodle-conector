import { createAppClient, type AppClient } from '../../integrations/http/api-client';

export type AutomationConfig = {
  dueDaysAhead?: number;
  maxStudentsToAnalyze?: number;
  maxAssignmentsToAnalyze?: number;
  inactivityThresholdDays?: number;
  minGradePercentage?: number;
  messageText?: string;
};

export type Automation = {
  id: string;
  ownerId: string;
  connectionAlias?: string;
  courseId: string;
  name: string;
  description?: string;
  scheduleType: 'manual' | 'daily' | 'weekly' | string;
  runHourUtc: number;
  runMinuteUtc: number;
  runDayOfWeek?: number;
  conditionType: string;
  actionType: string;
  config: AutomationConfig;
  isEnabled: boolean;
  nextRunAt?: string;
  lastRunAt?: string;
  createdAt: string;
  updatedAt: string;
};

export type AutomationInput = {
  connectionAlias?: string;
  courseId: string;
  name: string;
  description?: string;
  scheduleType: 'manual' | 'daily' | 'weekly';
  runHourUtc: number;
  runMinuteUtc: number;
  runDayOfWeek?: number;
  conditionType: string;
  actionType: string;
  config?: AutomationConfig;
  isEnabled: boolean;
};

export type AutomationRun = {
  runId: string;
  automationId: string;
  status: string;
  trigger: string;
  attemptCount: number;
  createdActions: number;
  skippedActions: number;
  failedActions: number;
  pendingActionIds: string[];
  errorCode?: string;
  errorMessage?: string;
  scheduledFor: string;
  startedAt?: string;
  finishedAt?: string;
  summaryJson?: string;
};

type ListResponse<T> = { data: T[]; meta: { generatedAt: string; connectionRef?: string } };
type ItemResponse<T> = { data: T; meta: { generatedAt: string; connectionRef?: string } };

export const createAutomationsGateway = (client: AppClient = createAppClient()) => ({
  list: () => client.get<ListResponse<Automation>>('/api/automations'),
  create: (input: AutomationInput) => client.request<ItemResponse<Automation>>('/api/automations', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  }),
  update: (id: string, input: AutomationInput) => client.request<ItemResponse<Automation>>(`/api/automations/${encodeURIComponent(id)}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  }),
  remove: (id: string) => client.request<void>(`/api/automations/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  run: (id: string, force = true) => client.request<ItemResponse<AutomationRun>>(`/api/automations/${encodeURIComponent(id)}/run`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ force }),
  }),
  runs: (id: string) => client.get<ListResponse<AutomationRun>>(`/api/automations/${encodeURIComponent(id)}/runs?limit=20`),
});

export const automationsGateway = createAutomationsGateway();
