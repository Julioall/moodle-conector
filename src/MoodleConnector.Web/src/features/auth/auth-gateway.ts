import { createPortalClient } from '../../integrations/http/portal-client';

type AccountResponse = { ok?: boolean; error?: string };

const client = createPortalClient();

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
};
