import { createAppClient, type AppClient } from '../../integrations/http/api-client';
import type { PlannerReference } from '../tasks/tasks-gateway';
export type AgendaEvent = { id: string; title: string; description?: string; startAt: string; endAt?: string; type: string; createdAt: string; updatedAt: string; references?: PlannerReference[] };
export type AgendaInput = { title: string; description?: string; startAt: string; endAt?: string; type?: string; references?: PlannerReference[] };
export type AgendaResponse = { data: AgendaEvent[]; meta: { generatedAt: string } };
export type PlannerImportResult = { imported: number; updated: number; skipped: number; warnings: string[] };
export const createAgendaGateway = (client: AppClient = createAppClient()) => ({ list: () => client.get<AgendaResponse>('/api/agenda'), create: (input: AgendaInput) => client.request('/api/agenda', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }), update: (id: string, input: AgendaInput) => client.request(`/api/agenda/${encodeURIComponent(id)}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }), remove: (id: string) => client.request(`/api/agenda/${encodeURIComponent(id)}`, { method: 'DELETE' }), importIcs: (file: File) => { const form = new FormData(); form.append('file', file); return client.request<{ data: PlannerImportResult }>('/api/agenda/import', { method: 'POST', body: form }); } });
export const agendaGateway = createAgendaGateway();


