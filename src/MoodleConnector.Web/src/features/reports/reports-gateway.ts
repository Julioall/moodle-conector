import { createPortalClient, type PortalClient } from '../../integrations/http/portal-client';
export type OperationalReport = { data: { openTasks: number; completedTasks: number; upcomingEvents: number; followupsRecorded: number; generatedAt: string }; meta: { generatedAt: string } };
export const createReportsGateway = (client: PortalClient = createPortalClient()) => ({ operational: () => client.get<OperationalReport>('/api/portal/reports/operational') });
export const reportsGateway = createReportsGateway();
