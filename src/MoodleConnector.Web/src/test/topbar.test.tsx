import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeAll, describe, expect, it, vi } from 'vitest';
import { TopBar } from '../components/layout/TopBar';
import { connectionsGateway } from '../features/connections/connections-gateway';
import { SidebarProvider } from '../components/ui/sidebar';

vi.mock('../features/connections/connections-gateway', () => ({
  connectionsGateway: { list: vi.fn() },
}));

beforeAll(() => {
  const element = HTMLElement.prototype as HTMLElement & { hasPointerCapture?: () => boolean; setPointerCapture?: () => void; releasePointerCapture?: () => void };
  element.hasPointerCapture ??= () => false;
  element.setPointerCapture ??= () => undefined;
  element.releasePointerCapture ??= () => undefined;
  HTMLElement.prototype.scrollIntoView ??= () => undefined;
});

describe('TopBar', () => {
  it('loads real Moodle connections and exposes a scoped selector', async () => {
    vi.mocked(connectionsGateway.list).mockResolvedValue({
      data: [
        { connectionRef: 'goias', alias: 'SENAI Goiás', host: 'https://goias.example', status: 'unknown', isDefault: true, capabilities: ['read'] },
        { connectionRef: 'nacional', alias: 'SENAI Nacional', host: 'https://nacional.example', status: 'offline', isDefault: false, capabilities: ['read'] },
      ],
      meta: { generatedAt: '2026-08-10T00:00:00Z' },
    });
    const user = userEvent.setup();
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    render(<QueryClientProvider client={client}><SidebarProvider><MemoryRouter><TopBar /></MemoryRouter></SidebarProvider></QueryClientProvider>);

    expect((await screen.findAllByText('SENAI Goiás')).length).toBeGreaterThan(0);
    expect(screen.getByRole('combobox', { name: 'Selecionar Moodle' })).toBeInTheDocument();
    await user.click(screen.getByRole('combobox', { name: 'Selecionar Moodle' }));
    expect(await screen.findByText('SENAI Nacional')).toBeInTheDocument();
    await user.click(screen.getByText('SENAI Nacional'));
    await waitFor(() => expect(window.localStorage.getItem('app:selected-connection')).toBe('nacional'));
  });
});

