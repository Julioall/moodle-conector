import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

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

vi.mock('../features/followup/followup-gateway', () => ({
  followupGateway: {
    list: vi.fn(),
    create: vi.fn(),
  },
}));

import { CoursePanelPage } from '../features/courses/CoursePanelPage';
import { coursesGateway } from '../features/courses/courses-gateway';
import { studentsGateway } from '../features/students/students-gateway';
import { dashboardGateway } from '../features/dashboard/dashboard-gateway';
import { followupGateway } from '../features/followup/followup-gateway';

describe('CoursePanelPage', () => {
  afterEach(cleanup);

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
    vi.mocked(followupGateway.list).mockResolvedValue({
      data: [],
      meta: { page: 1, pageSize: 20, returned: 0, hasMore: false, generatedAt: '2026-08-14T00:00:00Z' },
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

    await waitFor(() => expect(studentsGateway.byCourse).toHaveBeenCalledWith('demo', '42', 1, 25, true));
  });

  it('shows correction status inside Activities without exposing a corrections tab', async () => {
    renderPage('/cursos/demo/42?tab=activities');

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Atividades' })).toBeInTheDocument());

    expect(screen.getByText('2 para corrigir')).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Pendências e correções' })).not.toBeInTheDocument();
    expect(dashboardGateway.get).toHaveBeenCalledWith('demo', '42');
  });

  it('keeps the course workspace to three tabs and opens contextual follow-up', async () => {
    renderPage();

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Estado da turma' })).toBeInTheDocument());

    expect(screen.getAllByRole('tab')).toHaveLength(3);
    expect(screen.queryByRole('tab', { name: 'Follow-up' })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Fóruns' })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Registrar acompanhamento' }));

    expect(screen.getByRole('dialog', { name: 'Registrar acompanhamento' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Aluno' })).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: 'Referência do aluno' })).not.toBeInTheDocument();
  });

  it('shows the shared intervention history in the overview', async () => {
    vi.mocked(followupGateway.list).mockResolvedValue({
      data: [{
        id: 'followup-1',
        studentRef: 'demo:7',
        studentName: 'Aluno acompanhado',
        courseRef: 'demo:42',
        kind: 'acompanhamento',
        reason: 'atividade_pendente',
        action: 'mensagem',
        status: 'em_acompanhamento',
        actorName: 'Tutor de teste',
        notes: 'Orientação enviada pelo tutor.',
        occurredAt: '2026-08-14T12:00:00Z',
        createdAt: '2026-08-14T12:00:00Z',
      }],
      meta: { page: 1, pageSize: 20, returned: 1, hasMore: false, generatedAt: '2026-08-14T00:00:00Z' },
    });

    renderPage();

    await waitFor(() => expect(screen.getByText('Histórico de acompanhamento')).toBeInTheDocument());
    expect(screen.getByText('Aluno acompanhado')).toBeInTheDocument();
    expect(screen.getByText(/por Tutor de teste/)).toBeInTheDocument();
    expect(screen.getByText('Mensagem')).toBeInTheDocument();
    expect(screen.queryByText('Atividades que precisam de ação')).not.toBeInTheDocument();
  });

  it('does not expose the technical student reference for legacy history records', async () => {
    vi.mocked(followupGateway.list).mockResolvedValue({
      data: [{
        id: 'followup-legacy',
        studentRef: 'demo:440754',
        courseRef: 'demo:42',
        kind: 'acompanhamento',
        action: 'mensagem',
        status: 'em_acompanhamento',
        notes: 'Mensagem registrada antes da persistência do nome.',
        occurredAt: '2026-08-14T12:00:00Z',
        createdAt: '2026-08-14T12:00:00Z',
      }],
      meta: { page: 1, pageSize: 20, returned: 1, hasMore: false, generatedAt: '2026-08-14T00:00:00Z' },
    });

    renderPage();

    await waitFor(() => expect(screen.getByText('Aluno 440754')).toBeInTheDocument());
    expect(screen.queryByText('demo:440754')).not.toBeInTheDocument();
  });
});
