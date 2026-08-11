import { useQuery } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { connectionsGateway, type MoodleConnection } from './connections-gateway';

const SELECTED_CONNECTION_KEY = 'app:selected-connection';

function readStoredConnection(): string | undefined {
  if (typeof window === 'undefined') return undefined;
  return window.localStorage.getItem(SELECTED_CONNECTION_KEY) || undefined;
}

export function useMoodleConnections() {
  return useQuery({
    queryKey: ['app', 'connections'],
    queryFn: connectionsGateway.list,
    staleTime: 60_000,
  });
}

/**
 * Keeps the selected Moodle in the URL when possible and in local storage
 * across screens. The absence of a value means the server's default Moodle;
 * there is intentionally no synthetic "all" scope.
 */
export function useConnectionScope() {
  const [params, setParams] = useSearchParams();
  const [storedConnection, setStoredConnection] = useState(readStoredConnection);
  const connections = useMoodleConnections();
  const urlConnection = params.get('connectionRef') || undefined;
  const defaultConnection = connections.data?.data.find((connection) => connection.isDefault)
    ?? connections.data?.data[0];
  const connectionRef = urlConnection ?? storedConnection;
  const selectedConnection = connections.data?.data.find((connection) => connection.connectionRef === connectionRef)
    ?? (!connectionRef ? defaultConnection : undefined);
  const effectiveConnectionRef = selectedConnection?.connectionRef
    ?? (connections.isSuccess ? defaultConnection?.connectionRef : connectionRef);

  const selectConnection = useCallback((nextConnectionRef: string) => {
    const next = nextConnectionRef || undefined;
    setStoredConnection(next);
    if (typeof window !== 'undefined') {
      if (next) window.localStorage.setItem(SELECTED_CONNECTION_KEY, next);
      else window.localStorage.removeItem(SELECTED_CONNECTION_KEY);
    }
    setParams((current) => {
      const nextParams = new URLSearchParams(current);
      if (next) nextParams.set('connectionRef', next);
      else nextParams.delete('connectionRef');
      return nextParams;
    }, { replace: true });
  }, [setParams]);

  return {
    connections,
    connectionRef: effectiveConnectionRef,
    selectedConnection,
    selectConnection,
  };
}

export function connectionDisplayName(connection?: MoodleConnection) {
  return connection?.alias || connection?.connectionRef || 'Moodle padrão';
}

