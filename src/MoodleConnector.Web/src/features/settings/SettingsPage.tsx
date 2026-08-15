import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { KeyRound, LogOut, MailPlus, Pencil, Plus, Save, ShieldCheck, UserRound, UsersRound } from 'lucide-react';
import { useSearchParams } from 'react-router-dom';

import { Badge } from '../../components/ui/badge';
import { Button } from '../../components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card';
import { Input } from '../../components/ui/input';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../../components/ui/tabs';
import { useSession } from '../auth/useSession';
import { accessGateway } from './access-gateway';
import { MessagePreferencesCard } from './MessagePreferencesCard';
import { ThemeCard } from './ThemeCard';
import { cn } from '../../lib/utils';

const gateway = accessGateway();

const permissionSectionDefinitions = [
  { title: 'Portal', permissions: ['dashboard.view', 'courses.view', 'schools.view', 'settings.view', 'admin.view', 'connections.manage'] },
  { title: 'Alunos e acompanhamento', permissions: ['students.view', 'students.followup.write', 'reports.view', 'messages.prepare'] },
  { title: 'Operação acadêmica', permissions: ['tasks.manage', 'agenda.manage', 'grading.view', 'grading.manage', 'automations.view', 'automations.manage'] },
] as const;

const permissionLabels: Record<string, string> = {
  'dashboard.view': 'Resumo da semana',
  'courses.view': 'Meus cursos',
  'schools.view': 'Escolas',
  'settings.view': 'Configurações',
  'admin.view': 'Administração',
  'connections.manage': 'Gerenciar conexões',
  'students.view': 'Alunos dos cursos',
  'students.followup.write': 'Registrar follow-up',
  'reports.view': 'Relatórios',
  'messages.prepare': 'Preparar mensagens',
  'tasks.manage': 'Tarefas',
  'agenda.manage': 'Agenda',
  'grading.view': 'Consultar correções',
  'grading.manage': 'Gerenciar correções',
  'automations.view': 'Consultar automações',
  'automations.manage': 'Gerenciar automações',
};

function permissionLabel(permission: string) {
  return permissionLabels[permission] ?? permission.replace(/^tool\./, '').replaceAll('.', ' ');
}

export function SettingsPage() {
  const client = useQueryClient();
  const { user, logout, isAdmin } = useSession();
  const [searchParams] = useSearchParams();
  const [activeTab, setActiveTab] = useState(searchParams.get('tab') === 'acesso' ? 'acesso' : 'geral');
  const [activeAccessTab, setActiveAccessTab] = useState(searchParams.get('section') === 'grupos' ? 'grupos' : 'equipes');
  const [editingGroupId, setEditingGroupId] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [selected, setSelected] = useState<string[]>([]);
  const [email, setEmail] = useState('');
  const [role, setRole] = useState('member');
  const teams = useQuery({ queryKey: ['app', 'settings', 'teams'], queryFn: gateway.teams, staleTime: 60_000, enabled: activeTab === 'acesso' && activeAccessTab === 'equipes' });
  const groups = useQuery({ queryKey: ['app', 'settings', 'groups'], queryFn: gateway.groups, staleTime: 60_000, enabled: activeTab === 'acesso' && activeAccessTab === 'grupos' });
  const catalog = useQuery({ queryKey: ['app', 'settings', 'permission-catalog'], queryFn: gateway.catalog, staleTime: 300_000, enabled: activeTab === 'acesso' && activeAccessTab === 'grupos' });
  const saveGroup = useMutation({
    mutationFn: () => {
      const input = { name: name.trim(), description: description.trim() || undefined, permissions: selected };
      return editingGroupId ? gateway.updateGroup(editingGroupId, input) : gateway.createGroup(input);
    },
    onSuccess: (result) => {
      setEditingGroupId(result.group.id);
      setName(result.group.name);
      setDescription(result.group.description);
      setSelected([...result.group.permissions]);
      void client.invalidateQueries({ queryKey: ['app', 'settings', 'groups'] });
    },
  });
  const invite = useMutation({ mutationFn: () => gateway.invite(teams.data?.teams.find((team) => !team.isPersonal)?.id ?? teams.data?.teams[0]?.id ?? '', { email: email.trim(), role: role.trim() }), onSuccess: () => setEmail('') });
  const inviteTeam = teams.data?.teams.find((team) => !team.isPersonal) ?? teams.data?.teams[0];
  const togglePermission = (permission: string) => setSelected((current) => current.includes(permission) ? current.filter((item) => item !== permission) : [...current, permission]);
  const startNewGroup = () => { setEditingGroupId(null); setName(''); setDescription(''); setSelected([]); saveGroup.reset(); };
  const startEditingGroup = (group: { id: string; name: string; description: string; permissions: string[] }) => { setEditingGroupId(group.id); setName(group.name); setDescription(group.description); setSelected([...group.permissions]); saveGroup.reset(); };
  const permissionSections: { title: string; permissions: string[] }[] = permissionSectionDefinitions
    .map((section) => ({ ...section, permissions: section.permissions.filter((permission) => catalog.data?.permissions.includes(permission)) }))
    .filter((section) => section.permissions.length > 0);
  const knownPermissions = new Set(permissionSectionDefinitions.flatMap((section) => section.permissions));
  const toolPermissions = (catalog.data?.permissions ?? []).filter((permission) => permission.startsWith('tool.') && !knownPermissions.has(permission));
  if (toolPermissions.length > 0) permissionSections.push({ title: 'Ferramentas do conector', permissions: toolPermissions });
  const remainingPermissions = (catalog.data?.permissions ?? []).filter((permission) => !knownPermissions.has(permission) && !permission.startsWith('tool.'));
  if (remainingPermissions.length > 0) permissionSections.push({ title: 'Outras permissões', permissions: remainingPermissions });
  const initials = user?.name?.split(' ').filter(Boolean).slice(0, 2).map((part) => part[0]).join('').toUpperCase() || 'CL';

  return (
    <main className="space-y-6 animate-fade-in" aria-labelledby="settings-title">
      <header className="page-heading"><div><p className="eyebrow">ADMINISTRAÇÃO</p><h1 id="settings-title">Configurações</h1><p>Gerencie sua conta, equipes e regras de acesso da plataforma.</p></div>{isAdmin && <Badge variant="outline" className="w-fit gap-1.5"><ShieldCheck className="h-3.5 w-3.5 text-status-success" />Administrador</Badge>}</header>

      <Tabs value={activeTab} onValueChange={setActiveTab} className="space-y-5">
        <TabsList className="h-auto w-full justify-start gap-1 overflow-x-auto">
          <TabsTrigger value="geral" className="gap-2"><UserRound className="h-4 w-4" />Geral</TabsTrigger>
          <TabsTrigger value="mensagens" className="gap-2"><MailPlus className="h-4 w-4" />Mensagens</TabsTrigger>
          <TabsTrigger value="acesso" className="gap-2"><ShieldCheck className="h-4 w-4" />Acesso</TabsTrigger>
        </TabsList>

        <TabsContent value="geral" className="mt-0 space-y-6">
          <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(300px,0.7fr)]">
            <Card><CardHeader><CardTitle className="flex items-center gap-2 text-lg"><UserRound className="h-5 w-5" />Perfil</CardTitle><CardDescription>Informações da sua conta Claris.</CardDescription></CardHeader><CardContent><div className="flex items-center gap-4"><div className="flex h-16 w-16 items-center justify-center rounded-full bg-primary/10 text-xl font-bold text-primary">{initials}</div><div><p className="text-lg font-medium">{user?.name ?? 'Usuário Claris'}</p><p className="text-sm text-muted-foreground">Conta autenticada pelo Moodle Connector</p></div></div></CardContent></Card>
            <Card><CardHeader><CardTitle className="text-lg">Função e acesso</CardTitle><CardDescription>Permissões efetivas no portal.</CardDescription></CardHeader><CardContent><div className="space-y-3">{user?.roles?.map((item) => <div key={item} className="flex items-center justify-between border-b pb-3 last:border-0"><span className="text-sm text-muted-foreground">Perfil</span><Badge variant="secondary">{item}</Badge></div>)}<div className="flex items-center justify-between"><span className="text-sm text-muted-foreground">Permissões</span><span className="font-semibold">{user?.permissions?.length ?? 0}</span></div></div></CardContent></Card>
          </div>
          <ThemeCard />
          <Card><CardHeader><CardTitle className="text-lg">Como as configurações funcionam</CardTitle><CardDescription>As regras são aplicadas no servidor e refletidas nesta interface.</CardDescription></CardHeader><CardContent className="grid gap-4 text-sm text-muted-foreground md:grid-cols-3"><div className="rounded-lg border bg-muted/20 p-4"><p className="font-medium text-foreground">Moodle conectado</p><p className="mt-1">Cursos e estudantes continuam vindo das conexões configuradas no portal.</p></div><div className="rounded-lg border bg-muted/20 p-4"><p className="font-medium text-foreground">Acesso por grupo</p><p className="mt-1">Prefira grupos para regras permanentes e exceções individuais apenas quando necessário.</p></div><div className="rounded-lg border bg-muted/20 p-4"><p className="font-medium text-foreground">Ações auditáveis</p><p className="mt-1">Envios e alterações sensíveis exigem confirmação e ficam registrados.</p></div></CardContent></Card>
        </TabsContent>

        <TabsContent value="mensagens" className="mt-0 space-y-6"><MessagePreferencesCard /></TabsContent>

        <TabsContent value="acesso" className="mt-0 space-y-5">
          <Tabs value={activeAccessTab} onValueChange={setActiveAccessTab} className="space-y-5">
            <TabsList className="h-auto w-full justify-start gap-1 overflow-x-auto">
              <TabsTrigger value="equipes" className="gap-2"><UsersRound className="h-4 w-4" />Equipes</TabsTrigger>
              <TabsTrigger value="grupos" className="gap-2"><KeyRound className="h-4 w-4" />Grupos de acesso</TabsTrigger>
              <TabsTrigger value="seguranca" className="gap-2"><ShieldCheck className="h-4 w-4" />Segurança</TabsTrigger>
            </TabsList>
            <TabsContent value="equipes" className="mt-0 space-y-6">
          <Card><CardHeader><CardTitle className="flex items-center gap-2 text-lg"><UsersRound className="h-5 w-5" />Equipes</CardTitle><CardDescription>Organize as pessoas que podem acessar o conector.</CardDescription></CardHeader><CardContent>{teams.isPending && <p className="text-sm text-muted-foreground">Carregando equipes…</p>}{teams.isError && <p className="text-sm text-destructive" role="alert">Não foi possível carregar as equipes.</p>}{teams.data?.teams.length === 0 && <p className="text-sm text-muted-foreground">Nenhuma equipe disponível.</p>}{teams.data?.teams.map((team) => <article key={team.id} className="dashboard-row"><div><strong>{team.name}</strong><span>{team.isPersonal ? 'Equipe pessoal' : 'Equipe compartilhada'} · {team.scopes.length} escopos</span></div><Badge variant="outline">{team.role}</Badge></article>)}</CardContent></Card>
          <Card><CardHeader><CardTitle className="flex items-center gap-2 text-lg"><MailPlus className="h-5 w-5" />Convidar membro</CardTitle><CardDescription>O convite será enviado para a equipe compartilhada selecionada.</CardDescription></CardHeader><CardContent><form className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_180px_auto] sm:items-end" onSubmit={(event) => { event.preventDefault(); if (inviteTeam && email.trim() && role.trim()) invite.mutate(); }}><label className="grid gap-1.5 text-sm font-medium">E-mail<Input type="email" placeholder="email@exemplo.com" value={email} onChange={(event) => setEmail(event.target.value)} required /></label><label className="grid gap-1.5 text-sm font-medium">Papel<Input placeholder="member" value={role} onChange={(event) => setRole(event.target.value)} required /></label><Button type="submit" disabled={!inviteTeam || invite.isPending}><MailPlus />{invite.isPending ? 'Enviando…' : 'Enviar convite'}</Button></form>{invite.isSuccess && <p className="mt-4 text-sm text-status-success">Convite criado com sucesso.</p>}{invite.isError && <p className="mt-4 text-sm text-destructive" role="alert">Não foi possível enviar o convite.</p>}</CardContent></Card>
        </TabsContent>

        <TabsContent value="grupos" className="mt-0 space-y-5">
          <Card className="border-primary/20 bg-primary/[0.03]">
            <CardContent className="flex items-start gap-3 p-4">
              <KeyRound className="mt-0.5 h-5 w-5 shrink-0 text-primary" />
              <div><p className="font-medium">Funções prontas para ajustar</p><p className="mt-1 text-sm text-muted-foreground">Tutor e Monitor já vêm como modelos editáveis. As permissões só passam a valer quando o grupo for atribuído a alguém.</p></div>
            </CardContent>
          </Card>

          <div className="grid gap-6 xl:grid-cols-[minmax(260px,0.72fr)_minmax(0,1.28fr)]">
            <Card className="h-fit">
              <CardHeader className="flex-row items-start justify-between gap-3 space-y-0">
                <div><CardTitle className="text-lg">Grupos de acesso</CardTitle><CardDescription>Selecione um grupo para revisar ou editar suas regras.</CardDescription></div>
                <Badge variant="secondary">{groups.data?.groups.length ?? 0}</Badge>
              </CardHeader>
              <CardContent className="space-y-2">
                {groups.isPending && <p className="text-sm text-muted-foreground">Carregando grupos…</p>}
                {groups.isError && <p className="text-sm text-destructive" role="alert">Não foi possível carregar os grupos.</p>}
                {groups.data?.groups.length === 0 && <p className="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">Nenhum grupo criado ainda.</p>}
                {groups.data?.groups.map((group) => {
                  const isCommon = group.name === 'Tutor' || group.name === 'Monitor';
                  const isSelected = editingGroupId === group.id;
                  return <button key={group.id} type="button" aria-pressed={isSelected} onClick={() => startEditingGroup(group)} className={cn('w-full rounded-lg border p-4 text-left transition-colors hover:border-primary/40 hover:bg-muted/30', isSelected ? 'border-primary bg-primary/5 shadow-sm' : 'border-border bg-card')}>
                    <div className="flex items-start justify-between gap-3"><div className="min-w-0"><p className="font-semibold">{group.name}</p><p className="mt-1 line-clamp-2 text-sm text-muted-foreground">{group.description || 'Sem descrição'}</p></div><Pencil className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" /></div>
                    <div className="mt-3 flex flex-wrap items-center gap-2"><Badge variant="outline">{group.permissions.length} permissões</Badge>{isCommon && <Badge variant="secondary">Modelo padrão</Badge>}</div>
                  </button>;
                })}
              </CardContent>
            </Card>

            <Card>
              <CardHeader className="flex-row items-start justify-between gap-3 space-y-0">
                <div><CardTitle className="text-lg">{editingGroupId ? 'Editar grupo' : 'Novo grupo'}</CardTitle><CardDescription>{editingGroupId ? 'Ajuste o nome, a descrição e as permissões deste grupo.' : 'Crie uma função reutilizável para a sua equipe.'}</CardDescription></div>
                {editingGroupId && <Button type="button" variant="outline" size="sm" onClick={startNewGroup}><Plus />Novo grupo</Button>}
              </CardHeader>
              <CardContent>
                <form className="grid gap-5" onSubmit={(event) => { event.preventDefault(); if (name.trim()) saveGroup.mutate(); }}>
                  <div className="grid gap-4 sm:grid-cols-2">
                    <label className="grid gap-1.5 text-sm font-medium">Nome<Input value={name} onChange={(event) => setName(event.target.value)} placeholder="Ex.: Tutor" maxLength={120} required /></label>
                    <label className="grid gap-1.5 text-sm font-medium">Descrição<Input value={description} onChange={(event) => setDescription(event.target.value)} placeholder="Acesso da equipe de tutoria" maxLength={500} /></label>
                  </div>

                  <div className="space-y-3">
                    <div><p className="text-sm font-medium">Permissões do grupo</p><p className="mt-1 text-xs text-muted-foreground">Escolha apenas o que essa função precisa para trabalhar.</p></div>
                    {catalog.isPending && <p className="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">Carregando catálogo…</p>}
                    <div className="grid gap-3 md:grid-cols-2">
                      {permissionSections.map((section) => <fieldset key={section.title} className="rounded-lg border bg-muted/10 p-3"><legend className="px-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">{section.title}</legend><div className="mt-2 grid gap-2">{section.permissions.map((permission) => <label key={permission} className="flex cursor-pointer items-start gap-2 rounded-md border border-transparent bg-card px-2.5 py-2 text-sm transition-colors hover:border-primary/25 hover:bg-primary/5"><input type="checkbox" aria-label={permissionLabel(permission)} checked={selected.includes(permission)} onChange={() => togglePermission(permission)} className="mt-0.5 h-4 w-4 accent-primary" /><span className="min-w-0"><span className="block font-medium">{permissionLabel(permission)}</span><span className="block truncate text-[11px] text-muted-foreground">{permission}</span></span></label>)}</div></fieldset>)}
                    </div>
                  </div>

                  <div className="flex flex-col gap-3 border-t pt-4 sm:flex-row sm:items-center sm:justify-between"><div><p className="text-sm font-medium">{selected.length} permissões selecionadas</p><p className="text-xs text-muted-foreground">As alterações afetam todos os membros deste grupo.</p></div><Button type="submit" disabled={!name.trim() || saveGroup.isPending}><Save />{saveGroup.isPending ? 'Salvando…' : editingGroupId ? 'Salvar alterações' : 'Criar grupo'}</Button></div>
                </form>
                {saveGroup.isError && <p className="mt-4 text-sm text-destructive" role="alert">Não foi possível salvar o grupo. Verifique se sua conta tem permissão de administração.</p>}
              </CardContent>
            </Card>
          </div>
        </TabsContent>

        <TabsContent value="seguranca" className="mt-0 space-y-6"><Card><CardHeader><CardTitle className="flex items-center gap-2 text-lg"><ShieldCheck className="h-5 w-5" />Segurança e permissões</CardTitle><CardDescription>As concessões são verificadas no servidor a cada ação protegida.</CardDescription></CardHeader><CardContent className="space-y-4"><div className="rounded-lg border bg-muted/20 p-4 text-sm text-muted-foreground"><p className="font-medium text-foreground">Princípio de menor privilégio</p><p className="mt-1">Conceda apenas as permissões necessárias para cada função e use grupos para manter a governança simples.</p></div><div className="flex items-center justify-between border-b pb-3"><span className="text-sm text-muted-foreground">Permissões na sua conta</span><span className="font-semibold">{user?.permissions?.length ?? 0}</span></div><div className="flex items-center justify-between"><span className="text-sm text-muted-foreground">Estado da sessão</span><Badge variant="secondary">Autenticada</Badge></div></CardContent></Card><Card className="border-destructive/30"><CardHeader><CardTitle className="flex items-center gap-2 text-lg text-destructive"><LogOut className="h-5 w-5" />Sair da conta</CardTitle><CardDescription>Encerre a sessão atual neste dispositivo.</CardDescription></CardHeader><CardContent><Button variant="destructive" className="w-full sm:w-auto" onClick={() => void logout()}><LogOut />Sair da conta</Button></CardContent></Card></TabsContent>
          </Tabs>
        </TabsContent>
      </Tabs>
    </main>
  );
}
