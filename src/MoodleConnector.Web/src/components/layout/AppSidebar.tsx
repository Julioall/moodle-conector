import {
  BookOpen,
  Building2,
  CalendarDays,
  CheckSquare,
  FileSpreadsheet,
  LayoutDashboard,
  LogOut,
  MessageSquare,
  Plug,
  Settings,
  Users,
  type LucideIcon,
} from 'lucide-react';
import { NavLink } from '@/components/NavLink';
import { Button } from '@/components/ui/button';
import { ClarisIcon } from '@/components/ui/claris-logo';
import { Separator } from '@/components/ui/separator';
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from '@/components/ui/sidebar';
import { useSession } from '@/features/auth/useSession';
import { APP_PERMISSIONS } from '@/lib/access-control';

type SidebarNavItem = {
  title: string;
  url: string;
  icon: LucideIcon;
  permission: string;
};

const operationalItems: SidebarNavItem[] = [
  { title: 'Resumo da Semana', url: '/', icon: LayoutDashboard, permission: APP_PERMISSIONS.DASHBOARD_VIEW },
  { title: 'Meus Cursos', url: '/meus-cursos', icon: BookOpen, permission: APP_PERMISSIONS.COURSES_CATALOG_VIEW },
  { title: 'Escolas', url: '/escolas', icon: Building2, permission: APP_PERMISSIONS.SCHOOLS_VIEW },
  { title: 'Alunos', url: '/alunos', icon: Users, permission: APP_PERMISSIONS.STUDENTS_VIEW },
  { title: 'Tarefas', url: '/tarefas', icon: CheckSquare, permission: APP_PERMISSIONS.TASKS_VIEW },
  { title: 'Agenda', url: '/agenda', icon: CalendarDays, permission: APP_PERMISSIONS.AGENDA_VIEW },
];

const communicationItems: SidebarNavItem[] = [
  { title: 'Mensagens', url: '/mensagens', icon: MessageSquare, permission: APP_PERMISSIONS.MESSAGES_VIEW },
];

const managementItems: SidebarNavItem[] = [
  { title: 'Relatórios', url: '/relatorios', icon: FileSpreadsheet, permission: APP_PERMISSIONS.REPORTS_VIEW },
  { title: 'Conexões Moodle', url: '/conexoes', icon: Plug, permission: APP_PERMISSIONS.SERVICES_VIEW },
];

const settingsItems: SidebarNavItem[] = [
  { title: 'Configurações', url: '/configuracoes', icon: Settings, permission: APP_PERMISSIONS.SETTINGS_VIEW },
];

export function AppSidebar() {
  const { user, logout, can } = useSession();
  const { state } = useSidebar();
  const isCollapsed = state === 'collapsed';

  const visibleItems = (items: SidebarNavItem[]) =>
    user ? items.filter((item) => can(item.permission)) : items;

  const renderGroup = (label: string, items: SidebarNavItem[]) => {
    const visible = visibleItems(items);
    if (visible.length === 0) return null;

    return (
      <SidebarGroup>
        <SidebarGroupLabel className="px-3 text-[10px] font-semibold uppercase tracking-[0.16em] text-sidebar-foreground/45">
          {!isCollapsed && label}
        </SidebarGroupLabel>
        <SidebarGroupContent>
          <SidebarMenu>
            {visible.map((item) => (
              <SidebarMenuItem key={item.title}>
                <SidebarMenuButton asChild tooltip={item.title}>
                  <NavLink
                    to={item.url}
                    end={item.url === '/'}
                    className="flex items-center gap-3 rounded-md px-3 py-2 text-sm text-sidebar-foreground/75 transition-colors hover:bg-sidebar-accent hover:text-sidebar-foreground"
                    activeClassName="bg-sidebar-accent text-sidebar-primary font-medium"
                  >
                    <item.icon className="h-4 w-4 shrink-0" />
                    {!isCollapsed && <span>{item.title}</span>}
                  </NavLink>
                </SidebarMenuButton>
              </SidebarMenuItem>
            ))}
          </SidebarMenu>
        </SidebarGroupContent>
      </SidebarGroup>
    );
  };

  return (
    <Sidebar collapsible="icon" className="border-r border-sidebar-border">
      <SidebarHeader className="p-4">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 items-center justify-center overflow-hidden rounded-lg text-primary">
            <ClarisIcon className="h-full w-full" />
          </div>
          {!isCollapsed && (
            <div className="flex min-w-0 flex-col">
              <span className="truncate font-semibold text-sidebar-foreground">Moodle Connector</span>
              <span className="text-xs text-sidebar-foreground/60">App acadêmico</span>
            </div>
          )}
        </div>
      </SidebarHeader>

      <SidebarContent>
        {renderGroup('Operacional', operationalItems)}
        <Separator className="my-2 bg-sidebar-border" />
        {renderGroup('Comunicação', communicationItems)}
        {renderGroup('Gestão', managementItems)}
        <Separator className="my-2 bg-sidebar-border" />
        {renderGroup('Configurações', settingsItems)}
      </SidebarContent>

      <SidebarFooter className="p-4">
        {user && (
          <div className="flex items-center gap-3">
            <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-sidebar-accent text-sm font-medium uppercase text-sidebar-foreground">
              {user.name.charAt(0)}
            </div>
            {!isCollapsed && (
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium text-sidebar-foreground">{user.name}</p>
                <p className="truncate text-xs text-sidebar-foreground/60">{user.roles?.[0] || 'Usuário'}</p>
              </div>
            )}
            <Button
              variant="ghost"
              size="icon"
              onClick={logout}
              className="h-8 w-8 shrink-0 text-sidebar-foreground/60 hover:bg-sidebar-accent hover:text-sidebar-foreground"
              title="Sair"
            >
              <LogOut className="h-4 w-4" />
            </Button>
          </div>
        )}
      </SidebarFooter>
    </Sidebar>
  );
}

