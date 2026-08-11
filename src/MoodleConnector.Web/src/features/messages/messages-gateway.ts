import { createAppClient, type AppClient } from '../../integrations/http/api-client';
export type MessagePreview = { messageType: string; courseId: string; recipientCount: number; recipients: { studentId: string; fullName: string }[]; messageText: string; selectionCriteria: string; confirmationText: string; expiresAt: string; risks: string[]; pendingActionId?: string };
export type MessageInput = { courseId: string; messageType: string; recipientIds: string[]; customText?: string };
export type MessageResult = { status: string; pendingActionId: string; sentCount: number; failedCount: number; warnings: string[] };
export const createMessagesGateway = (client: AppClient = createAppClient()) => ({ prepare: (input: MessageInput) => client.request<{ data: MessagePreview }>('/api/messages/prepare', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) }), confirm: async (pendingActionId: string, confirmationText: string) => { await client.get('/api/csrf'); return client.request<{ data: MessageResult }>('/api/messages/confirm', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ pendingActionId, confirmationText }) }); } });
export const messagesGateway = createMessagesGateway();


