import { createPortalClient } from '../../integrations/http/portal-client';

export type MoodleConnection = {
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
};
