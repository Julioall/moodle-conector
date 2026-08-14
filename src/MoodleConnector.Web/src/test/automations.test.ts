import { describe, expect, it, vi } from 'vitest';

import { createAutomationsGateway } from '../features/automations/automations-gateway';

describe('automations gateway', () => {
  it('creates a Moodle-first definition through the portal contract', async () => {
    const request = vi.fn().mockResolvedValue({ data: { id: 'automation-1' } });
    const gateway = createAutomationsGateway({ get: vi.fn(), request });
    const input = {
      connectionAlias: 'campus-a',
      courseId: '42',
      name: 'Atividades vencidas',
      scheduleType: 'daily' as const,
      runHourUtc: 12,
      runMinuteUtc: 0,
      conditionType: 'overdue_submissions',
      actionType: 'create_tasks',
      isEnabled: true,
    };

    await gateway.create(input);

    expect(request).toHaveBeenCalledWith('/api/automations', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify(input),
    }));
  });

  it('runs and reads history through the explicit automation endpoints', async () => {
    const request = vi.fn().mockResolvedValue({ data: { runId: 'run-1' } });
    const get = vi.fn().mockResolvedValue({ data: [] });
    const gateway = createAutomationsGateway({ get, request });

    await gateway.run('automation-1');
    await gateway.runs('automation-1');

    expect(request).toHaveBeenCalledWith('/api/automations/automation-1/run', expect.objectContaining({ method: 'POST' }));
    expect(get).toHaveBeenCalledWith('/api/automations/automation-1/runs?limit=20');
  });
});
