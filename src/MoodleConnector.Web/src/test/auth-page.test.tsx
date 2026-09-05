import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { AuthPage } from '../features/auth/AuthPage';
import { authGateway } from '../features/auth/auth-gateway';

vi.mock('../features/auth/auth-gateway', () => ({
  authGateway: {
    login: vi.fn(),
    register: vi.fn(),
    connectMoodle: vi.fn(),
  },
}));

describe('AuthPage', () => {
  afterEach(() => {
    window.history.replaceState({}, '', '/');
    vi.clearAllMocks();
  });

  it('offers account registration and continues to the first Moodle connection', async () => {
    window.history.replaceState({}, '', '/?tab=register');
    vi.mocked(authGateway.register).mockResolvedValue({ ok: true });
    const user = userEvent.setup();

    render(<AuthPage />);

    await user.type(screen.getByLabelText('Nome'), 'Tutor Moodle Conector');
    await user.type(screen.getByLabelText('E-mail'), 'tutor@example.com');
    await user.type(screen.getByLabelText('Senha'), 'senha-segura-123');
    await user.type(screen.getByLabelText('Confirmar senha'), 'senha-segura-123');
    await user.click(screen.getByRole('button', { name: 'Criar conta' }));

    await expect(authGateway.register).toHaveBeenCalledWith('Tutor Moodle Conector', 'tutor@example.com', 'senha-segura-123');
    expect(await screen.findByRole('heading', { name: 'Conectar o primeiro Moodle' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Conectar Moodle' })).toBeInTheDocument();
  });
});
