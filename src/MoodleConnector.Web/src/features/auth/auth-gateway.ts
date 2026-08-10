type AccountResponse = { ok?: boolean; error?: string };

async function accountRequest(path: string, body: Record<string, unknown>): Promise<AccountResponse> {
  const response = await fetch(path, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify(body),
  });
  const payload = await response.json() as AccountResponse;
  if (!response.ok) throw new Error(payload.error ?? 'Não foi possível concluir a operação.');
  return payload;
}

export const authGateway = {
  login: (email: string, password: string) => accountRequest('/api/account/login', { email, password }),
  register: (name: string, email: string, password: string) => accountRequest('/api/account/register', { name, email, password }),
};
