import { createPortalClient } from '../../integrations/http/portal-client';

export type MoodleConnection = {
  connectionId?: string;
  connectionRef: string;
  alias: string;
  host: string;
  status: 'active' | 'inactive' | 'unknown' | string;
  isDefault: boolean;
  capabilities: string[];
  lastValidatedAt?: string;
};

export type ConnectionsResponse = {
  data: MoodleConnection[];
  meta: {
    generatedAt: string;
  };
};

export const connectionsGateway = {
  list: (): Promise<ConnectionsResponse> =>
    createPortalClient().get<ConnectionsResponse>('/api/portal/connections'),
  connect: async (input: { moodleAlias: string; moodleBaseUrl: string; moodleUsername: string; moodlePassword: string; isDefault: boolean; canWrite: boolean }) => {
    return createPortalClient().request<{ error?: string }>('/api/portal/connections', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(input),
    });
  },
  validate: async (connectionId: string) =>
    createPortalClient().request<{ status: string; lastValidatedAt?: string }>(`/api/portal/connections/${encodeURIComponent(connectionId)}/validate`, { method: 'POST' }),
  update: async (connectionId: string, input: { moodleAlias: string; moodleBaseUrl: string; moodleUsername?: string; moodlePassword?: string; isDefault: boolean; canWrite: boolean }) =>
    createPortalClient().request<MoodleConnection>(`/api/portal/connections/${encodeURIComponent(connectionId)}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }),
  dataSummary: async (connectionId: string) => createPortalClient().get<{ memories: number; documents: number; moodleUserLinks: number; auditLogsRetained: number }>(`/api/portal/connections/${encodeURIComponent(connectionId)}/data-summary`),
  remove: async (connectionId: string, deleteLinkedData: boolean, confirmationText?: string) => createPortalClient().request<{ ok: boolean }>(`/api/portal/connections/${encodeURIComponent(connectionId)}`, { method: 'DELETE', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ deleteLinkedData, confirmationText }) }),
};
