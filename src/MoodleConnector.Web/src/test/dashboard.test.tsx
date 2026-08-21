import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { DashboardPage } from '../features/dashboard/DashboardPage';
import { dashboardGateway } from '../features/dashboard/dashboard-gateway';
import { coursesGateway } from '../features/courses/courses-gateway';

vi.mock('../features/dashboard/dashboard-gateway', async () => ({
  ...(await vi.importActual('../features/dashboard/dashboard-gateway')),
  dashboardGateway: { get: vi.fn(), getMetric: vi.fn() },
}));
vi.mock('../features/courses/courses-gateway', async () => ({
  ...(await vi.importActual('../features/courses/courses-gateway')),
  coursesGateway: { listAll: vi.fn() },
}));

describe('DashboardPage', () => {
  beforeEach(() => vi.clearAllMocks());

  it('loads each dashboard metric independently for the selected connection', async () => {
    vi.mocked(dashboardGateway.get).mockResolvedValue({
      data: { summary: { activeCourses: 0, pendingDeliveries: 0, awaitingGrading: 0, studentsAtRisk: 0, studentsNeedingAttention: 0 }, priorities: [], activitiesToReview: [], recentActivity: [], warnings: [] },
      meta: { generatedAt: '2026-08-10T00:00:00Z' },
    });
    vi.mocked(dashboardGateway.getMetric).mockImplementation(async (metric) => {
      if (metric === 'pending') return { data: { summary: { activeCourses: 1, pendingDeliveries: 3, awaitingGrading: 1, studentsAtRisk: 1, studentsNeedingAttention: 2, pendingCorrectionAssignments: 1 }, priorities: [], activitiesToReview: [], courseSummaries: [{ courseId: '42', courseName: 'Curso de demonstração', pendingCorrectionActivities: 1, pendingCorrectionSubmissions: 2, pendingSubmissionActivities: 1, pendingSubmissions: 3, studentsAwaitingCorrection: 2, studentsWithPendingSubmissions: 3, overdueSubmissions: 1, isTruncated: false }], todayItems: [], warnings: [] }, meta: { generatedAt: '2026-08-10T00:00:00Z' } };
      if (metric === 'access') return { data: { summary: { activeCourses: 1, pendingDeliveries: 0, awaitingGrading: 0, studentsAtRisk: 1, studentsNeedingAttention: 1, activeStudents: 4, activeNormalStudents: 3 }, segments: [{ key: 'recent', label: 'Acesso recente · 0–7 dias', students: 3, tone: 'success' }, { key: 'low', label: 'Baixo acesso · 8–14 dias', students: 0, tone: 'warning' }, { key: 'none', label: 'Sem acesso · 14+ dias ou nunca', students: 1, tone: 'risk' }], snapshots: [{ date: '2026-08-10', totalStudents: 4, recentStudents: 3, lowAccessStudents: 0, staleStudents: 0, neverAccessedStudents: 1, studentsAtRisk: 1 }], warnings: [] }, meta: { generatedAt: '2026-08-10T00:00:00Z' } };
      return { data: { summary: { activeCourses: 1, pendingDeliveries: 0, awaitingGrading: 0, studentsAtRisk: 0, studentsNeedingAttention: 0, todayEvents: 2, todayTasks: 1 }, warnings: [] }, meta: { generatedAt: '2026-08-10T00:00:00Z' } };
    });
    vi.mocked(coursesGateway.listAll).mockResolvedValue({ data: [], meta: { page: 1, pageSize: 100, returned: 0, hasMore: false, generatedAt: '2026-08-10T00:00:00Z' } });

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const view = render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/?connectionRef=demo']}><DashboardPage /></MemoryRouter></QueryClientProvider>);

    await waitFor(() => expect(screen.getByText('Eventos hoje')).toBeInTheDocument());
    await waitFor(() => expect(screen.getByText('Pendências por curso')).toBeInTheDocument());
    expect(screen.getByText('Meus Cursos', { exact: true })).toBeInTheDocument();
    expect(screen.getByText('Curso de demonstração')).toBeInTheDocument();
    expect(screen.getByText('Últimos 15 dias')).toBeInTheDocument();
    expect(screen.getByText('Alunos', { exact: true })).toBeInTheDocument();
    expect(screen.getByText('14+ dias', { exact: true })).toBeInTheDocument();
    expect(screen.getByText('Nunca', { exact: true })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Barras agrupadas' })).toHaveAttribute('aria-pressed', 'true');
    const lineChartButton = screen.getByRole('button', { name: 'Linhas por faixa' });
    fireEvent.click(lineChartButton);
    expect(lineChartButton).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Área empilhada' })).toHaveAttribute('aria-pressed', 'false');
    expect(dashboardGateway.getMetric).toHaveBeenCalledWith('summary', 'demo');
    expect(dashboardGateway.getMetric).toHaveBeenCalledWith('pending', 'demo');
    expect(dashboardGateway.getMetric).toHaveBeenCalledWith('access', 'demo');
    expect(dashboardGateway.getMetric).not.toHaveBeenCalledWith('courses', 'demo');

    const callsAfterInitialLoad = vi.mocked(dashboardGateway.getMetric).mock.calls.length;
    view.unmount();
    render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/?connectionRef=demo']}><DashboardPage /></MemoryRouter></QueryClientProvider>);
    await waitFor(() => expect(screen.getByText('Pendências por curso')).toBeInTheDocument());
    expect(dashboardGateway.getMetric).toHaveBeenCalledTimes(callsAfterInitialLoad);
  });
});
