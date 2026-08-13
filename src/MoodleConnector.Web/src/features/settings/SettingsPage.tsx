import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '../../components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/card';
import { accessGateway } from './access-gateway';

const gateway = accessGateway();

export function SettingsPage() {
  const client = useQueryClient();
  const teams = useQuery({ queryKey: ['app', 'settings', 'teams'], queryFn: gateway.teams });
  const groups = useQuery({ queryKey: ['app', 'settings', 'groups'], queryFn: gateway.groups });
  const catalog = useQuery({ queryKey: ['app', 'settings', 'permission-catalog'], queryFn: gateway.catalog });
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [selected, setSelected] = useState<string[]>([]);
  const [email, setEmail] = useState('');
  const [role, setRole] = useState('member');

  const createGroup = useMutation({
    mutationFn: () => gateway.createGroup({ name, description, permissions: selected }),
    onSuccess: () => { setName(''); setDescription(''); setSelected([]); void client.invalidateQueries({ queryKey: ['app', 'settings', 'groups'] }); }
  });
  const invite = useMutation({
    mutationFn: () => gateway.invite(teams.data?.teams.find(team => !team.isPersonal)?.id ?? teams.data?.teams[0]?.id ?? '', { email, role }),
    onSuccess: () => setEmail('')
  });

  const togglePermission = (permission: string) => setSelected(current => current.includes(permission) ? current.filter(item => item !== permission) : [...current, permission]);
  const inviteTeam = teams.data?.teams.find(team => !team.isPersonal) ?? teams.data?.teams[0];

  return <main className="content-frame"><header className="page-heading"><div><p className="eyebrow">ADMINISTRAÇÃO</p><h1>Configurações</h1><p>Gerencie equipes, grupos de acesso e permissões da plataforma.</p></div></header>
    <section className="grid gap-6 lg:grid-cols-2">
      <Card><CardHeader><CardTitle>Grupos de permissão</CardTitle></CardHeader><CardContent className="space-y-4">
        {groups.isPending && <p>Carregando grupos.</p>}{groups.isError && <p role="alert">Não foi possível carregar os grupos.</p>}
        {groups.data?.groups.map(group => <article className="dashboard-row" key={group.id}><div><strong>{group.name}</strong><span>{group.description || 'Sem descrição'}</span></div><span>{group.permissions.length} permissões</span></article>)}
        <div className="space-y-3 border-t pt-4"><h2 className="font-medium">Criar grupo</h2><input aria-label="Nome do grupo" className="ui-input w-full" placeholder="Ex.: Tutores" value={name} onChange={event => setName(event.target.value)} /><input aria-label="Descrição do grupo" className="ui-input w-full" placeholder="Descrição" value={description} onChange={event => setDescription(event.target.value)} /><div className="grid gap-2 sm:grid-cols-2">{catalog.data?.permissions.map(permission => <label className="flex items-center gap-2 text-sm" key={permission}><input type="checkbox" checked={selected.includes(permission)} onChange={() => togglePermission(permission)} />{permission}</label>)}</div><Button disabled={!name.trim() || createGroup.isPending} onClick={() => createGroup.mutate()}>Criar grupo</Button>{createGroup.isError && <p role="alert">Não foi possível criar o grupo. Verifique se sua conta é administradora.</p>}</div>
      </CardContent></Card>
      <Card><CardHeader><CardTitle>Equipes e convites</CardTitle></CardHeader><CardContent className="space-y-4">
        {teams.isPending && <p>Carregando equipes.</p>}{teams.data?.teams.map(team => <article className="dashboard-row" key={team.id}><div><strong>{team.name}</strong><span>{team.isPersonal ? 'Equipe pessoal' : 'Equipe compartilhada'}</span></div><span>{team.role}</span></article>)}
        <div className="space-y-3 border-t pt-4"><h2 className="font-medium">Convidar membro</h2><input aria-label="E-mail do convite" className="ui-input w-full" type="email" placeholder="email@exemplo.com" value={email} onChange={event => setEmail(event.target.value)} /><input aria-label="Papel do convite" className="ui-input w-full" placeholder="Ex.: monitor" value={role} onChange={event => setRole(event.target.value)} /><Button disabled={!inviteTeam || !email.trim() || !role.trim() || invite.isPending} onClick={() => invite.mutate()}>Enviar convite</Button>{invite.isSuccess && <p>Convite criado com sucesso.</p>}{invite.isError && <p role="alert">Não foi possível enviar o convite.</p>}</div>
      </CardContent></Card>
    </section>
    <Card><CardHeader><CardTitle>Segurança</CardTitle></CardHeader><CardContent><p>As permissões são aplicadas no servidor. Concessões e bloqueios individuais devem ser usados apenas para exceções; prefira grupos para regras permanentes.</p></CardContent></Card>
  </main>;
}
