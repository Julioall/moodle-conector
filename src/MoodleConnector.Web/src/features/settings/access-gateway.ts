import { createAppClient, type AppClient } from '../../integrations/http/api-client';

export type Team = { id: string; name: string; isPersonal: boolean; role: string; scopes: string[] };
export type PermissionGroup = { id: string; name: string; description: string; permissions: string[] };

export const accessGateway = (client: AppClient = createAppClient()) => ({
  teams: () => client.get<{ teams: Team[] }>('/api/teams'),
  groups: () => client.get<{ groups: PermissionGroup[] }>('/api/permission-groups'),
  catalog: () => client.get<{ permissions: string[] }>('/api/permission-catalog'),
  createGroup: async (input: { name: string; description?: string; permissions: string[] }) => {
    await client.get('/api/csrf');
    return client.request<{ group: PermissionGroup }>('/api/permission-groups', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input)
    });
  },
  invite: async (teamId: string, input: { email: string; role: string }) => {
    await client.get('/api/csrf');
    return client.request(`/api/teams/${encodeURIComponent(teamId)}/invitations`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input)
    });
  }
});
