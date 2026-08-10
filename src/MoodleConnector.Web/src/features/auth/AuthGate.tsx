import { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { sessionGateway } from '../../integrations/auth/session-gateway';

export function AuthGate({ children }: { children: ReactNode }) {
  const session = useQuery({ queryKey: ['portal', 'session'], queryFn: sessionGateway.getSession, retry: false });
  if (session.isLoading) return <div className="foundation-card">Carregando sessão…</div>;
  if (session.error) return <div className="foundation-card"><h1>Sessão indisponível</h1><p>Não foi possível validar sua sessão.</p></div>;
  if (!session.data?.authenticated) return <div className="foundation-card"><h1>Login necessário</h1><a href="/auth/login?returnUrl=/">Entrar no Moodle Connector</a></div>;
  return <>{children}</>;
}
