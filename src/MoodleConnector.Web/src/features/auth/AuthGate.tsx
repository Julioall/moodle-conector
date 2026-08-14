import { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';

import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { ClarisLogo } from '@/components/ui/claris-logo';
import { sessionGateway } from '../../integrations/auth/session-gateway';
import { AppHttpError } from '../../integrations/http/api-client';
import { AuthPage } from './AuthPage';

export function AuthGate({ children }: { children: ReactNode }) {
  const session = useQuery({ queryKey: ['app', 'session'], queryFn: sessionGateway.getSession, retry: false });

  if (session.isLoading) {
    return <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-background via-background to-accent/20 p-4"><ClarisLogo className="w-60 animate-pulse text-primary" /></div>;
  }

  if (session.error && !(session.error instanceof AppHttpError && session.error.status === 401)) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-background via-background to-accent/20 p-4">
        <Card className="w-full max-w-md">
          <CardContent className="space-y-4 p-6">
            <ClarisLogo className="mx-auto w-48 text-primary" />
            <h1 className="text-xl font-semibold">Sessão indisponível</h1>
            <p className="text-sm text-muted-foreground">Não foi possível validar sua sessão.</p>
            <Button variant="outline" onClick={() => void session.refetch()}>Tentar novamente</Button>
          </CardContent>
        </Card>
      </div>
    );
  }

  if (!session.data?.data.authenticated) return <AuthPage />;
  return <>{children}</>;
}
