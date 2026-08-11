import { createAppClient } from '../http-client';

export type AppSession = { data: { authenticated: boolean; user?: { id: string; name: string; roles: string[]; permissions: string[] } }; meta: { generatedAt: string; connectionRef?: string } };
export const sessionGateway = {
  getSession: () => createAppClient().get<AppSession>('/api/session'),
};

