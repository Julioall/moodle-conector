import { createAppClient, type AppClient } from '../../integrations/http/api-client';
export type TaskStatus = 'todo' | 'in_progress' | 'done';
export type TaskPriority = 'low' | 'medium' | 'high' | 'urgent';
export type Task = { id: string; title: string; description?: string; status: TaskStatus; priority: TaskPriority; dueAt?: string; createdAt: string; updatedAt: string };
export type TaskInput = { title?: string; description?: string; status?: TaskStatus; priority?: TaskPriority; dueAt?: string };
export type TaskList = { data: Task[]; meta: { page: number; pageSize: number; returned: number; total?: number; hasMore: boolean; generatedAt: string } };
export type TaskResponse = { data: Task; meta: { generatedAt: string } };
export const createTasksGateway = (client: AppClient = createAppClient()) => ({
  list: (page = 1, pageSize = 20) => client.get<TaskList>(`/api/tasks?page=${page}&pageSize=${pageSize}`),
  create: async (input: TaskInput) => { await client.get('/api/csrf'); return client.request<TaskResponse>('/api/tasks', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }); },
  update: async (id: string, input: TaskInput) => { await client.get('/api/csrf'); return client.request<TaskResponse>(`/api/tasks/${encodeURIComponent(id)}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }); },
  remove: async (id: string) => { await client.get('/api/csrf'); return client.request<void>(`/api/tasks/${encodeURIComponent(id)}`, { method: 'DELETE' }); },
});
export const tasksGateway = createTasksGateway();



