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
    const response = await fetch('/api/account/connect-moodle', { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', Accept: 'application/json' }, body: JSON.stringify(input) });
    const payload = await response.json() as { error?: string };
    if (!response.ok) throw new Error(payload.error ?? 'Não foi possível cadastrar a conexão.');
    return payload;
  },
};
