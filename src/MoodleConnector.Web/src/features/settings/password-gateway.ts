import { createAppClient } from '../../integrations/http/api-client';

export type PortalAccount = { id: string; name: string; email: string; createdAtUtc: string };

export const passwordGateway = {
  change: (currentPassword: string, newPassword: string) => createAppClient().request<{ message?: string }>('/api/account/password', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ currentPassword, newPassword }) }),
  listAccounts: () => createAppClient().get<{ accounts: PortalAccount[] }>('/api/admin/accounts'),
  resetToDefault: (userId: string) => createAppClient().request<{ message?: string }>(`/api/admin/accounts/${encodeURIComponent(userId)}/reset-password`, { method: 'POST', headers: { 'Content-Type': 'application/json' } }),
};
