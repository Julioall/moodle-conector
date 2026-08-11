import { createAppClient, type AppClient } from '../../integrations/http-client';
export type Followup = { id: string; studentRef: string; courseRef?: string; kind: string; notes: string; occurredAt: string; createdAt: string };
export type FollowupInput = { studentRef: string; courseRef?: string; kind: string; notes: string; occurredAt?: string };
export type FollowupList = { data: Followup[]; meta: { page: number; pageSize: number; returned: number; hasMore: boolean; generatedAt: string } };
export const createFollowupGateway = (client: AppClient = createAppClient()) => ({ list: () => client.get<FollowupList>('/api/followups'), create: async (input: FollowupInput) => { await client.get('/api/csrf'); return client.request('/api/followups', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }); } });
export const followupGateway = createFollowupGateway();

