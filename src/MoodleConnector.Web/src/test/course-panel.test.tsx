import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../features/courses/courses-gateway', () => ({ coursesGateway: { get: vi.fn() } }));
vi.mock('../features/students/students-gateway', () => ({ studentsGateway: { byCourse: vi.fn() } }));

import { CoursePanelPage } from '../features/courses/CoursePanelPage';
import { coursesGateway } from '../features/courses/courses-gateway';
import { studentsGateway } from '../features/students/students-gateway';

describe('CoursePanelPage', () => {
  afterEach(cleanup);

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(coursesGateway.get).mockResolvedValue({
      data: { connectionRef: 'demo', courseId: '42', fullName: 'Curso de demonstração', displayName: 'Curso de demonstração' },
      meta: { generatedAt: '2026-08-14T00:00:00Z' },
    });
    vi.mocked(studentsGateway.byCourse).mockResolvedValue({
      data: [{ connectionRef: 'demo', studentId: 'student-1', name: 'Aluno teste', lastCourseAccessAt: '2026-08-14T08:00:00Z', risk: 'normal', riskFactors: [], studentRef: { connectionRef: 'demo', studentId: 'student-1' }, courses: [] }],
      meta: { page: 1, pageSize: 25, returned: 1, total: 1, hasMore: false, generatedAt: '2026-08-14T00:00:00Z' },
    });
  });

  function renderPage(initialEntry = '/cursos/demo/42') {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(<QueryClientProvider client={queryClient}><MemoryRouter initialEntries={[initialEntry]}><Routes><Route path="/cursos/:connectionRef/:courseId" element={<CoursePanelPage />} /></Routes></MemoryRouter></QueryClientProvider>);
  }

  it('shows the student list without the retired corrections view', async () => {
    renderPage();

    await screen.findByRole('heading', { name: 'Curso de demonstração' });
    expect(await screen.findByText('Aluno teste')).toBeInTheDocument();
    expect(screen.queryByText('Correções pendentes')).not.toBeInTheDocument();
  });

  it('loads the student list for the selected course', async () => {
    renderPage();

    await screen.findByRole('heading', { name: 'Curso de demonstração' });
    await waitFor(() => expect(studentsGateway.byCourse).toHaveBeenCalledWith('demo', '42', 1, 25));
    expect(screen.getByText('Aluno teste')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver notas' })).toHaveAttribute('href', '/cursos/demo/42/alunos/student-1');
  });

  it('refreshes the selected course data', async () => {
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Atualizar' }));

    await waitFor(() => expect(coursesGateway.get).toHaveBeenLastCalledWith('demo', '42', true));
  });
});
