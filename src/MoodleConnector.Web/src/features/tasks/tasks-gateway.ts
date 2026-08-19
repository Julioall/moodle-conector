import { createAppClient, type AppClient } from '../../integrations/http/api-client';
export type TaskStatus = 'todo' | 'in_progress' | 'done';
export type TaskPriority = 'low' | 'medium' | 'high' | 'urgent';
export type Task = { id: string; title: string; description?: string; status: TaskStatus; priority: TaskPriority; startAt?: string; dueAt?: string; createdAt: string; updatedAt: string };
export type TaskInput = { title?: string; description?: string; status?: TaskStatus; priority?: TaskPriority; startAt?: string; dueAt?: string };
export type TaskList = { data: Task[]; meta: { page: number; pageSize: number; returned: number; total?: number; hasMore: boolean; generatedAt: string } };
export type TaskResponse = { data: Task; meta: { generatedAt: string } };
export const createTasksGateway = (client: AppClient = createAppClient()) => ({
  list: (page = 1, pageSize = 24, status?: TaskStatus, priority?: TaskPriority) => {
    const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
    if (status) params.set('status', status);
    if (priority) params.set('priority', priority);
    return client.get<TaskList>(`/api/tasks?${params.toString()}`);
  },
  create: (input: TaskInput) => client.request<TaskResponse>('/api/tasks', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }),
  update: (id: string, input: TaskInput) => client.request<TaskResponse>(`/api/tasks/${encodeURIComponent(id)}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }),
  remove: (id: string) => client.request<void>(`/api/tasks/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  removeMany: (ids: string[]) => client.request<{ data: { requested: number; deleted: number }; meta: { generatedAt: string } }>('/api/tasks', { method: 'DELETE', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ids }) }),
});
export const tasksGateway = createTasksGateway();



