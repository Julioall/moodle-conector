import { describe, expect, it, vi } from 'vitest';

import { createEvidenceGateway, createSubmissionsGateway } from '../features/corrections/submissions-gateway';
import { createForumsGateway } from '../features/forums/forums-gateway';

describe('Moodle-first academic surfaces', () => {
  it('keeps submission scope and manual grade approval explicit', async () => {
    const get = vi.fn().mockResolvedValue({ data: { submissions: [] } });
    const request = vi.fn().mockResolvedValue({ data: { pendingActionId: 'pending-1' } });
    const gateway = createSubmissionsGateway({ get, request });

    await gateway.list('campus-a', '42', 'assignment-7');
    await gateway.prepareGrade({ connectionRef: 'campus-a', courseId: '42', assignmentId: 'assignment-7', studentId: 'student-3', proposedGrade: 8.5, justificationText: 'Critério da rubrica.' });
    await gateway.confirmGrade({ connectionRef: 'campus-a', pendingActionId: 'pending-1', confirmationText: 'CONFIRMAR NOTA 8.50' });

    expect(get).toHaveBeenCalledWith(expect.stringContaining('/api/submissions?'));
    expect(request).toHaveBeenNthCalledWith(1, '/api/grading/individual/prepare', expect.objectContaining({ method: 'POST' }));
    expect(request).toHaveBeenNthCalledWith(2, '/api/grading/individual/confirm', expect.objectContaining({ method: 'POST' }));
  });

  it('keeps forum reads and evidence history scoped to the selected Moodle', async () => {
    const get = vi.fn().mockResolvedValue({ data: [] });
    const gateway = createForumsGateway({ get, request: vi.fn() });
    const evidence = createEvidenceGateway({ get, request: vi.fn() });

    await gateway.list('campus-a', '42');
    await gateway.read('campus-a', '42', 'forum-2');
    await evidence.list('campus-a', '42');

    expect(get).toHaveBeenNthCalledWith(1, '/api/courses/campus-a/42/forums');
    expect(get).toHaveBeenNthCalledWith(2, expect.stringContaining('/api/courses/campus-a/42/forums/forum-2'));
    expect(get).toHaveBeenNthCalledWith(3, expect.stringContaining('/api/evidence?'));
  });
});
