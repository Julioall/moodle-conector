import { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { sessionGateway } from '../../integrations/auth/session-gateway';
import { AppHttpError } from '../../integrations/http/api-client';
import { AuthPage } from './AuthPage';

export function AuthGate({ children }: { children: ReactNode }) {
  const session = useQuery({ queryKey: ['app', 'session'], queryFn: sessionGateway.getSession, retry: false });
  if (session.isLoading) return <div className="foundation-card">Carregando sessÃ£oâ€¦</div>;
  if (session.error && !(session.error instanceof AppHttpError && session.error.status === 401)) return <div className="foundation-card"><h1>SessÃ£o indisponÃ­vel</h1><p>NÃ£o foi possÃ­vel validar sua sessÃ£o.</p><button className="ui-button ui-button-outline" onClick={() => void session.refetch()}>Tentar novamente</button></div>;
  if (!session.data?.data.authenticated) return <AuthPage />;
  return <>{children}</>;
}


