import { createAppClient, type AppClient } from '../../integrations/http/api-client';
export type AgendaEvent = { id: string; title: string; description?: string; startAt: string; endAt?: string; type: string; createdAt: string; updatedAt: string };
export type AgendaInput = { title: string; description?: string; startAt: string; endAt?: string; type?: string };
export type AgendaResponse = { data: AgendaEvent[]; meta: { generatedAt: string } };
export const createAgendaGateway = (client: AppClient = createAppClient()) => ({ list: () => client.get<AgendaResponse>('/api/agenda'), create: async (input: AgendaInput) => { await client.get('/api/csrf'); return client.request('/api/agenda', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }); }, remove: async (id: string) => { await client.get('/api/csrf'); return client.request(`/api/agenda/${encodeURIComponent(id)}`, { method: 'DELETE' }); } });
export const agendaGateway = createAgendaGateway();


