import { useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Bell, CheckCheck, Pencil, RefreshCw, Wifi, WifiOff } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { ScrollArea } from '@/components/ui/scroll-area';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { SidebarTrigger } from '@/components/ui/sidebar';
import { Switch } from '@/components/ui/switch';
import { cn } from '@/lib/utils';
import { MoodleIcon } from '@/components/ui/MoodleIcon';
import { dashboardGateway } from '@/features/dashboard/dashboard-gateway';
import { connectionDisplayName, useConnectionScope } from '@/features/connections/useConnectionScope';
import { GlobalActivityLine } from './GlobalActivityLine';

const statusLabels: Record<string, string> = {
  active: 'Ativa',
  online: 'Online',
  inactive: 'Inativa',
  offline: 'Offline',
  needs_reauth: 'Reautenticação necessária',
  unknown: 'Status desconhecido',
};

const LAST_SEEN_KEY = 'app:notifications-last-seen';

function formatLastValidation(value?: string) {
  if (!value) return 'Nunca';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Nunca' : date.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

function formatActivityDate(value?: string) {
  if (!value) return 'Agora';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Agora';
  const minutes = Math.round((date.getTime() - Date.now()) / 60_000);
  if (Math.abs(minutes) < 1) return 'Agora';
  if (Math.abs(minutes) < 60) return minutes < 0 ? `há ${Math.abs(minutes)} min` : `em ${minutes} min`;
  const hours = Math.round(minutes / 60);
  if (Math.abs(hours) < 24) return hours < 0 ? `há ${Math.abs(hours)} h` : `em ${hours} h`;
  const days = Math.round(hours / 24);
  return days < 0 ? `há ${Math.abs(days)} dia${Math.abs(days) === 1 ? '' : 's'}` : `em ${days} dia${days === 1 ? '' : 's'}`;
}

function readLastSeen() {
  if (typeof window === 'undefined') return undefined;
  return window.localStorage.getItem(LAST_SEEN_KEY) ?? undefined;
}

export function TopBar() {
  const queryClient = useQueryClient();
  const { connections, selectedConnection, connectionRef, selectConnection } = useConnectionScope();
  const [notificationOpen, setNotificationOpen] = useState(false);
  const [lastSeenAt, setLastSeenAt] = useState(readLastSeen);
  const [editMode, setEditMode] = useState(false);
  const status = selectedConnection?.status ?? 'unknown';
  const isOnline = status === 'online' || status === 'active';
  const isOffline = status === 'offline' || status === 'inactive';
  const activityQuery = useQuery({
    queryKey: ['app', 'topbar-activity', connectionRef],
    queryFn: () => dashboardGateway.get(connectionRef, undefined, true),
    enabled: notificationOpen && Boolean(connectionRef),
    staleTime: 30_000,
  });
  const activities = useMemo(() => activityQuery.data?.data.recentActivity ?? [], [activityQuery.data?.data.recentActivity]);
  const unreadCount = useMemo(() => {
    if (activities.length === 0) return 0;
    if (!lastSeenAt) return activities.length;
    const lastSeen = new Date(lastSeenAt).getTime();
    if (Number.isNaN(lastSeen)) return activities.length;
    return activities.filter((item) => item.occurredAt && new Date(item.occurredAt).getTime() > lastSeen).length;
  }, [activities, lastSeenAt]);

  const markNotificationsAsSeen = () => {
    const now = new Date().toISOString();
    setLastSeenAt(now);
    if (typeof window !== 'undefined') window.localStorage.setItem(LAST_SEEN_KEY, now);
  };

  const handleNotificationOpenChange = (open: boolean) => {
    setNotificationOpen(open);
    if (open) {
      void activityQuery.refetch();
      if (activities.length > 0) markNotificationsAsSeen();
    }
  };

  return (
    <header className="sticky top-0 relative z-30 flex h-14 items-center justify-end gap-4 border-b bg-card/95 px-4 backdrop-blur supports-[backdrop-filter]:bg-card/80 md:px-6">
      <SidebarTrigger className="md:hidden" />

      <div className="flex items-center gap-2">
        <div className="hidden items-center gap-2 text-xs text-muted-foreground md:flex">
          <span>Última sincronização:</span>
          <span className="font-medium">{formatLastValidation(selectedConnection?.lastValidatedAt)}</span>
        </div>

        {isOffline && (
          <div className="hidden items-center gap-1.5 rounded-md border border-amber-200 bg-amber-50 px-2.5 py-1.5 text-xs text-amber-700 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-300 sm:flex">
            <WifiOff className="h-3.5 w-3.5" />
            <span>Modo Offline</span>
          </div>
        )}

        <div className="hidden min-w-0 items-center gap-2 sm:flex">
          <span className="max-w-32 truncate text-sm font-medium lg:max-w-48">{connectionDisplayName(selectedConnection)}</span>
          <Badge variant="outline" className="hidden gap-1 text-[10px] font-normal lg:inline-flex">
            {isOnline ? <Wifi className="h-3 w-3 text-emerald-600" /> : <WifiOff className="h-3 w-3 text-muted-foreground" />}
            {statusLabels[status] ?? status}
          </Badge>
        </div>

        <label className="sr-only" htmlFor="global-moodle-selector">Moodle atual</label>
        <Select value={connectionRef ?? ''} onValueChange={selectConnection} disabled={connections.isPending || connections.data?.data.length === 0}>
          <SelectTrigger id="global-moodle-selector" aria-label="Selecionar Moodle" className="h-9 w-[170px] gap-2 text-xs sm:w-[220px]">
            <MoodleIcon className="h-4 w-4 shrink-0" />
            <SelectValue placeholder={connections.isPending ? 'Carregando Moodles…' : 'Selecionar Moodle'} />
          </SelectTrigger>
          <SelectContent>
            {connections.data?.data.map((connection) => (
              <SelectItem key={connection.connectionRef} value={connection.connectionRef}>
                <span className="flex items-center gap-2">
                  <span>{connection.alias}</span>
                  {connection.isDefault && <span className="text-[10px] text-muted-foreground">padrão</span>}
                </span>
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Button variant="ghost" size="icon" className="h-9 w-9" title="Atualizar conexões" aria-label="Atualizar conexões" onClick={() => { void queryClient.invalidateQueries({ queryKey: ['app', 'connections'] }); void queryClient.invalidateQueries({ queryKey: ['app', 'dashboard'] }); }}>
          <RefreshCw className={`h-4 w-4 ${connections.isFetching ? 'animate-spin' : ''}`} />
        </Button>

        <Popover open={notificationOpen} onOpenChange={handleNotificationOpenChange}>
          <PopoverTrigger asChild>
            <Button variant="ghost" size="icon" className="relative h-9 w-9" aria-label="Notificações">
              <Bell className="h-4 w-4" />
              {unreadCount > 0 && <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-destructive px-1 text-[10px] font-medium text-destructive-foreground">{unreadCount > 99 ? '99+' : unreadCount}</span>}
            </Button>
          </PopoverTrigger>
          <PopoverContent align="end" className="w-[340px] p-0">
            <div className="flex items-center justify-between border-b px-3 py-2">
              <div>
                <p className="text-sm font-medium">Notificações</p>
                <p className="text-xs text-muted-foreground">{activities.length} itens recentes</p>
              </div>
              <Button type="button" variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={markNotificationsAsSeen} disabled={activities.length === 0}>
                <CheckCheck className="mr-1 h-3.5 w-3.5" />Marcar lidas
              </Button>
            </div>
            <ScrollArea className="max-h-[360px]">
              {activityQuery.isFetching ? <div className="px-3 py-4 text-xs text-muted-foreground">Carregando notificações…</div> : activities.length === 0 ? <div className="px-3 py-4 text-xs text-muted-foreground">Nenhuma notificação no momento.</div> : <div className="divide-y">{activities.map((item) => <div key={item.key} className="px-3 py-2.5"><p className="text-sm font-medium leading-snug">{item.title}</p><p className="mt-1 text-xs leading-relaxed text-muted-foreground">{item.detail}</p><p className="mt-1 text-[11px] text-muted-foreground">{formatActivityDate(item.occurredAt)}</p></div>)}</div>}
            </ScrollArea>
          </PopoverContent>
        </Popover>

        <div className={cn('flex items-center gap-2 border-l border-border pl-2', editMode && 'text-primary')}>
          <Switch id="edit-mode" checked={editMode} onCheckedChange={setEditMode} />
          <Label htmlFor="edit-mode" className={cn('hidden cursor-pointer text-xs sm:inline', editMode ? 'font-medium text-primary' : 'text-muted-foreground')}>
            <Pencil className="mr-1 inline h-3 w-3" />Editar
          </Label>
        </div>
      </div>
      <GlobalActivityLine />
    </header>
  );
}
