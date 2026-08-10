import { createPortalClient, type PortalClient } from '../../integrations/http/portal-client';
export type AgendaEvent = { id: string; title: string; description?: string; startAt: string; endAt?: string; type: string; createdAt: string; updatedAt: string };
export type AgendaInput = { title: string; description?: string; startAt: string; endAt?: string; type?: string };
export type AgendaResponse = { data: AgendaEvent[]; meta: { generatedAt: string } };
export const createAgendaGateway = (client: PortalClient = createPortalClient()) => ({ list: () => client.get<AgendaResponse>('/api/portal/agenda'), create: async (input: AgendaInput) => { await client.get('/api/portal/csrf'); return client.request('/api/portal/agenda', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }); }, remove: async (id: string) => { await client.get('/api/portal/csrf'); return client.request(`/api/portal/agenda/${encodeURIComponent(id)}`, { method: 'DELETE' }); } });
export const agendaGateway = createAgendaGateway();
