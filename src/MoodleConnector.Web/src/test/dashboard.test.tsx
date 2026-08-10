import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { dashboardGateway } from '../features/dashboard/dashboard-gateway';
vi.mock('../features/dashboard/dashboard-gateway', async () => ({ ...(await vi.importActual('../features/dashboard/dashboard-gateway')), dashboardGateway: { get: vi.fn() } }));
describe('DashboardPage', () => { beforeEach(() => vi.clearAllMocks()); it('loads indicators with one dashboard request', async () => { vi.mocked(dashboardGateway.get).mockResolvedValue({ data: { summary: { activeCourses: 2, pendingDeliveries: 3, awaitingGrading: 0, studentsAtRisk: 1, studentsNeedingAttention: 2 }, priorities: [], activitiesToReview: [], recentActivity: [], warnings: [] }, meta: { generatedAt: '2026-08-10T00:00:00Z' } }); const client = new QueryClient({ defaultOptions: { queries: { retry: false } } }); render(<QueryClientProvider client={client}><MemoryRouter><DashboardPage /></MemoryRouter></QueryClientProvider>); await waitFor(() => expect(screen.getByText('Cursos em andamento')).toBeInTheDocument()); expect(screen.getAllByText('2')).toHaveLength(2); expect(dashboardGateway.get).toHaveBeenCalledTimes(1); }); });
