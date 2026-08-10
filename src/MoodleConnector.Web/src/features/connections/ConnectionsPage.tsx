import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Badge } from '../../components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/card';
import { Skeleton } from '../../components/ui/skeleton';
import { connectionsGateway, type MoodleConnection } from './connections-gateway';
import './connections-page.css';

const statusLabels: Record<string, string> = {
  active: 'Ativa',
  inactive: 'Inativa',
  unknown: 'Desconhecida',
};

function formatFreshness(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Atualização indisponível';
  return `Atualizado em ${new Intl.DateTimeFormat('pt-BR', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)}`;
}

function ConnectionCard({ connection }: { connection: MoodleConnection }) {
  return (
    <Card className="connection-card">
      <CardHeader>
        <div className="connection-card-heading">
          <div>
            <CardTitle>{connection.alias}</CardTitle>
            <span className="connection-ref">{connection.connectionRef}</span>
          </div>
          <Badge>{statusLabels[connection.status] ?? connection.status}</Badge>
        </div>
      </CardHeader>
      <CardContent>
        <p className="connection-host">{connection.host}</p>
        <div className="connection-details">
          {connection.isDefault && <Badge> padrão</Badge>}
          {connection.capabilities.map((capability) => (
            <Badge key={`${connection.connectionRef}-${capability}`}>{capability}</Badge>
          ))}
        </div>
        {connection.lastValidatedAt && (
          <p className="connection-validation">Validada em {formatFreshness(connection.lastValidatedAt).replace('Atualizado em ', '')}</p>
        )}
      </CardContent>
    </Card>
  );
}

export function ConnectionsPage() {
  const queryClient = useQueryClient();
  const [alias, setAlias] = useState('');
  const [baseUrl, setBaseUrl] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [isDefault, setIsDefault] = useState(true);
  const [canWrite, setCanWrite] = useState(false);
  const [success, setSuccess] = useState('');
  const query = useQuery({
    queryKey: ['portal', 'connections'],
    queryFn: connectionsGateway.list,
  });
  const mutation = useMutation({ mutationFn: connectionsGateway.connect, onSuccess: () => { setSuccess('Conexão cadastrada com sucesso.'); setAlias(''); setBaseUrl(''); setUsername(''); setPassword(''); void queryClient.invalidateQueries({ queryKey: ['portal', 'connections'] }); } });

  return (
    <main className="content-frame connections-page">
      <header className="page-heading">
        <div>
          <p className="eyebrow">GESTÃO</p>
          <h1>Conexões Moodle</h1>
          <p>Consulte os Moodles disponíveis e o estado de cada conexão.</p>
        </div>
        {query.data?.meta.generatedAt && <span className="freshness">{formatFreshness(query.data.meta.generatedAt)}</span>}
      </header>

      <Card className="connection-management"><CardHeader><CardTitle>Adicionar conexão Moodle</CardTitle><p>Cadastre o acesso do Moodle que será usado pelo portal.</p></CardHeader><CardContent><form className="connection-form" onSubmit={event => { event.preventDefault(); setSuccess(''); mutation.mutate({ moodleAlias: alias, moodleBaseUrl: baseUrl, moodleUsername: username, moodlePassword: password, isDefault, canWrite }); }}><div className="form-grid"><label>Nome da conexão<input required value={alias} onChange={event => setAlias(event.target.value)} /></label><label>URL base do Moodle<input required type="url" placeholder="https://moodle.exemplo.com" value={baseUrl} onChange={event => setBaseUrl(event.target.value)} /></label><label>Usuário Moodle<input required value={username} onChange={event => setUsername(event.target.value)} /></label><label>Senha Moodle<input required type="password" value={password} onChange={event => setPassword(event.target.value)} /></label></div><div className="form-checks"><label><input type="checkbox" checked={isDefault} onChange={event => setIsDefault(event.target.checked)} /> Usar como padrão</label><label><input type="checkbox" checked={canWrite} onChange={event => setCanWrite(event.target.checked)} /> Permitir operações de escrita</label></div>{mutation.isError && <p className="auth-error" role="alert">{mutation.error instanceof Error ? mutation.error.message : 'Não foi possível cadastrar a conexão.'}</p>}<div className="form-actions"><button className="auth-submit" type="submit" disabled={mutation.isPending}>{mutation.isPending ? 'Validando…' : 'Cadastrar conexão'}</button>{success && <p className="form-success" role="status">{success}</p>}</div></form></CardContent></Card>

      {query.isPending && (
        <div className="connections-grid" aria-label="Carregando conexões">
          <Skeleton className="connection-skeleton" />
          <Skeleton className="connection-skeleton" />
        </div>
      )}

      {query.isError && (
        <Card><CardContent><p role="alert">Não foi possível carregar as conexões Moodle.</p></CardContent></Card>
      )}

      {query.isSuccess && query.data.data.length === 0 && (
        <Card><CardContent><p>Nenhuma conexão Moodle disponível.</p></CardContent></Card>
      )}

      {query.isSuccess && query.data.data.length > 0 && (
        <section className="connections-grid" aria-label="Conexões Moodle">
          {query.data.data.map((connection) => <ConnectionCard key={connection.connectionRef} connection={connection} />)}
        </section>
      )}
    </main>
  );
}
