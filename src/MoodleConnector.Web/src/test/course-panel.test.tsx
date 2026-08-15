import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../features/courses/courses-gateway', () => ({
  coursesGateway: {
    get: vi.fn(),
    activities: vi.fn(),
  },
}));

vi.mock('../features/students/students-gateway', () => ({
  studentsGateway: {
    byCourse: vi.fn(),
  },
}));

vi.mock('../features/dashboard/dashboard-gateway', () => ({
  dashboardGateway: {
    get: vi.fn(),
  },
}));

import { CoursePanelPage } from '../features/courses/CoursePanelPage';
import { coursesGateway } from '../features/courses/courses-gateway';
import { studentsGateway } from '../features/students/students-gateway';
import { dashboardGateway } from '../features/dashboard/dashboard-gateway';

describe('CoursePanelPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(coursesGateway.get).mockResolvedValue({
      data: {
        connectionRef: 'demo',
        courseId: '42',
        fullName: 'Curso de demonstração',
        displayName: 'Curso de demonstração',
      },
      meta: { generatedAt: '2026-08-14T00:00:00Z' },
    });
    vi.mocked(coursesGateway.activities).mockResolvedValue({
      data: [],
      meta: { page: 1, pageSize: 20, returned: 0, total: 0, hasMore: false, generatedAt: '2026-08-14T00:00:00Z' },
    });
    vi.mocked(studentsGateway.byCourse).mockResolvedValue({
      data: [],
      meta: { page: 1, pageSize: 25, returned: 0, total: 0, hasMore: false, generatedAt: '2026-08-14T00:00:00Z' },
    });
    vi.mocked(dashboardGateway.get).mockResolvedValue({
      data: {
        summary: { activeCourses: 1, pendingDeliveries: 0, awaitingGrading: 2, studentsAtRisk: 0, studentsNeedingAttention: 0, activitiesToReview: 2, pendingCorrectionAssignments: 2 },
        priorities: [],
        activitiesToReview: [],
        recentActivity: [],
        warnings: [],
      },
      meta: { generatedAt: '2026-08-14T00:00:00Z' },
    });
  });

  function renderPage(initialEntry = '/cursos/demo/42') {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[initialEntry]}>
          <Routes>
            <Route path="/cursos/:connectionRef/:courseId" element={<CoursePanelPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
  }

  it('does not request the course roster until the Alunos tab is opened', async () => {
    renderPage();

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Curso de demonstração' })).toBeInTheDocument());
    expect(studentsGateway.byCourse).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('tab', { name: 'Alunos' }));

    await waitFor(() => expect(studentsGateway.byCourse).toHaveBeenCalledWith('demo', '42', 1, 25));
  });

  it('shows correction status inside Activities without exposing a corrections tab', async () => {
    renderPage('/cursos/demo/42?tab=activities');

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Atividades' })).toBeInTheDocument());

    expect(screen.getByText('2 para corrigir')).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Pendências e correções' })).not.toBeInTheDocument();
    expect(dashboardGateway.get).toHaveBeenCalledWith('demo', '42');
  });
});
