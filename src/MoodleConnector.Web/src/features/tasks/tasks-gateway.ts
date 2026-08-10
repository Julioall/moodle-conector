import { createPortalClient, type PortalClient } from '../../integrations/http/portal-client';
export type TaskStatus = 'todo' | 'in_progress' | 'done';
export type TaskPriority = 'low' | 'medium' | 'high' | 'urgent';
export type PortalTask = { id: string; title: string; description?: string; status: TaskStatus; priority: TaskPriority; dueAt?: string; createdAt: string; updatedAt: string };
export type TaskInput = { title?: string; description?: string; status?: TaskStatus; priority?: TaskPriority; dueAt?: string };
export type TaskList = { data: PortalTask[]; meta: { page: number; pageSize: number; returned: number; total?: number; hasMore: boolean; generatedAt: string } };
export type TaskResponse = { data: PortalTask; meta: { generatedAt: string } };
export const createTasksGateway = (client: PortalClient = createPortalClient()) => ({
  list: (page = 1, pageSize = 20) => client.get<TaskList>(`/api/portal/tasks?page=${page}&pageSize=${pageSize}`),
  create: async (input: TaskInput) => { await client.get('/api/portal/csrf'); return client.request<TaskResponse>('/api/portal/tasks', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }); },
  update: async (id: string, input: TaskInput) => { await client.get('/api/portal/csrf'); return client.request<TaskResponse>(`/api/portal/tasks/${encodeURIComponent(id)}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }); },
  remove: async (id: string) => { await client.get('/api/portal/csrf'); return client.request<void>(`/api/portal/tasks/${encodeURIComponent(id)}`, { method: 'DELETE' }); },
});
export const tasksGateway = createTasksGateway();
