import { createAppClient, type AppClient } from '../../integrations/http/api-client';
import type { PlannerReference } from '../tasks/tasks-gateway';
export type AgendaEvent = { id: string; title: string; description?: string; startAt: string; endAt?: string; type: string; createdAt: string; updatedAt: string; references?: PlannerReference[] };
export type AgendaInput = { title: string; description?: string; startAt: string; endAt?: string; type?: string; references?: PlannerReference[] };
export type AgendaResponse = { data: AgendaEvent[]; meta: { generatedAt: string } };
export const createAgendaGateway = (client: AppClient = createAppClient()) => ({ list: () => client.get<AgendaResponse>('/api/agenda'), create: async (input: AgendaInput) => { await client.get('/api/csrf'); return client.request('/api/agenda', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }); }, update: async (id: string, input: AgendaInput) => { await client.get('/api/csrf'); return client.request(`/api/agenda/${encodeURIComponent(id)}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }); }, remove: async (id: string) => { await client.get('/api/csrf'); return client.request(`/api/agenda/${encodeURIComponent(id)}`, { method: 'DELETE' }); }, importIcs: async (file: File) => { const form = new FormData(); form.append('file', file); return client.request<{ data: { imported: number; updated: number; skipped: number; warnings: string[] } }>('/api/agenda/import', { method: 'POST', body: form }); } });
export const agendaGateway = createAgendaGateway();


