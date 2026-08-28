import { createAppClient } from '../../integrations/http/api-client';

export type PortalAccount = { id: string; name: string; email: string; createdAtUtc: string };
export type AdminMetrics = {
  generatedAt: string; periodHours: number; totalRequests: number; failedRequests: number; averageDurationMs: number; activeEndpoints: number;
  endpoints: { endpoint: string; method: string; requests: number; errors: number; averageDurationMs: number }[];
  tools: { toolName: string; invocations: number; errors: number; averageDurationMs: number }[];
  errors: { occurredAt: string; source: string; operation: string; code: string; statusCode?: number; durationMs: number }[];
};

export const passwordGateway = {
  change: (currentPassword: string, newPassword: string) => createAppClient().request<{ message?: string }>('/api/account/password', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ currentPassword, newPassword }) }),
  listAccounts: () => createAppClient().get<{ accounts: PortalAccount[] }>('/api/admin/accounts'),
  resetToDefault: (userId: string) => createAppClient().request<{ message?: string }>(`/api/admin/accounts/${encodeURIComponent(userId)}/reset-password`, { method: 'POST', headers: { 'Content-Type': 'application/json' } }),
  metrics: () => createAppClient().get<AdminMetrics>('/api/admin/metrics?hours=168'),
};
