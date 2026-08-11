import { useQuery, useQueryClient } from '@tanstack/react-query';
import { sessionGateway } from '../../integrations/auth/session-gateway';

export function useSession() {
  const queryClient = useQueryClient();
  const session = useQuery({
    queryKey: ['app', 'session'],
    queryFn: sessionGateway.getSession,
    staleTime: 5 * 60 * 1000,
    retry: false
  });

  const logout = () => {
    // The app and the account/OAuth broker share the same account cookie.
    // Use the existing sign-out endpoint so the shell never invents a second auth flow.
    queryClient.clear();
    window.location.href = '/auth/logout';
  };

  const user = session.data?.data?.user;
  const isAuthenticated = session.data?.data?.authenticated ?? false;

  const can = (permission: string) => {
    if (!user) return false;
    return user.permissions?.includes(permission) || user.roles?.includes('admin');
  };

  const isAdmin = user?.roles?.includes('admin') ?? false;

  return {
    session,
    user,
    isAuthenticated,
    can,
    isAdmin,
    logout
  };
}

