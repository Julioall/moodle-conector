import { createPortalClient } from '../http/portal-client';

export type PortalSession = { data: { authenticated: boolean; user?: { id: string; name: string; roles: string[]; permissions: string[] } }; meta: { generatedAt: string; connectionRef?: string } };
export const sessionGateway = {
  getSession: () => createPortalClient().get<PortalSession>('/api/portal/session'),
};
