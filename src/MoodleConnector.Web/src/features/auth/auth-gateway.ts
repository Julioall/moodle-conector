import { createAppClient } from '../../integrations/http/api-client';

export type AccountResponse = {
  ok?: boolean;
  error?: string;
  hasMoodleConnected?: boolean;
  redirectUrl?: string;
};

export type MoodleBootstrapInput = {
  moodleAlias: string;
  moodleBaseUrl: string;
  moodleUsername: string;
  moodlePassword: string;
  isDefault: boolean;
  canWrite: boolean;
};

const client = createAppClient();

async function accountRequest(path: string, body: Record<string, unknown>): Promise<AccountResponse> {
  return client.request<AccountResponse>(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

export const authGateway = {
  login: (email: string, password: string) => accountRequest('/api/account/login', { email, password }),
  register: (name: string, email: string, password: string) => accountRequest('/api/account/register', { name, email, password }),
  connectMoodle: (input: MoodleBootstrapInput) => accountRequest('/api/account/connect-moodle', input),
};


