import { createPortalClient } from '../http/portal-client';

export type PortalSession = { authenticated: boolean; user?: { id: string; name: string; roles: string[]; permissions: string[] } };
export const sessionGateway = {
  getSession: () => createPortalClient().get<PortalSession>('/api/portal/session'),
};
