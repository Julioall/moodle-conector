import { useQueryClient } from '@tanstack/react-query';
import { RefreshCw, Wifi, WifiOff } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { MoodleIcon } from '@/components/ui/MoodleIcon';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { SidebarTrigger } from '@/components/ui/sidebar';
import { connectionDisplayName, useConnectionScope } from '@/features/connections/useConnectionScope';

const statusLabels: Record<string, string> = {
  active: 'Ativa',
  online: 'Online',
  inactive: 'Inativa',
  offline: 'Offline',
  needs_reauth: 'ReautenticaÃ§Ã£o necessÃ¡ria',
  unknown: 'Status desconhecido',
};

export function TopBar() {
  const queryClient = useQueryClient();
  const { connections, selectedConnection, connectionRef, selectConnection } = useConnectionScope();
  const status = selectedConnection?.status ?? 'unknown';
  const isOnline = status === 'online' || status === 'active';

  return (
    <header className="sticky top-0 z-30 flex min-h-14 items-center gap-3 border-b bg-card/95 px-4 backdrop-blur supports-[backdrop-filter]:bg-card/80 md:px-6">
      <SidebarTrigger />
      <div className="hidden min-w-0 flex-1 items-center gap-2 sm:flex">
        <span className="truncate text-sm font-medium">{connectionDisplayName(selectedConnection)}</span>
        <Badge variant="outline" className="hidden gap-1 text-[10px] font-normal lg:inline-flex">
          {isOnline ? <Wifi className="h-3 w-3 text-emerald-600" /> : <WifiOff className="h-3 w-3 text-muted-foreground" />}
          {statusLabels[status] ?? status}
        </Badge>
      </div>

      <div className="ml-auto flex items-center gap-2">
        <label className="sr-only" htmlFor="global-moodle-selector">Moodle atual</label>
        <Select
          value={connectionRef ?? ''}
          onValueChange={selectConnection}
          disabled={connections.isPending || connections.data?.data.length === 0}
        >
          <SelectTrigger id="global-moodle-selector" aria-label="Selecionar Moodle" className="h-9 w-[170px] gap-2 text-xs sm:w-[220px]">
            <MoodleIcon className="h-4 w-4 shrink-0" />
            <SelectValue placeholder={connections.isPending ? 'Carregando Moodlesâ€¦' : 'Selecionar Moodle'} />
          </SelectTrigger>
          <SelectContent>
            {connections.data?.data.map((connection) => (
              <SelectItem key={connection.connectionRef} value={connection.connectionRef}>
                <span className="flex items-center gap-2">
                  <span>{connection.alias}</span>
                  {connection.isDefault && <span className="text-[10px] text-muted-foreground">padrÃ£o</span>}
                </span>
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Button
          variant="ghost"
          size="icon"
          className="h-9 w-9"
          title="Atualizar conexÃµes"
          aria-label="Atualizar conexÃµes"
          onClick={() => void queryClient.invalidateQueries({ queryKey: ['app', 'connections'] })}
        >
          <RefreshCw className={`h-4 w-4 ${connections.isFetching ? 'animate-spin' : ''}`} />
        </Button>
      </div>
    </header>
  );
}

