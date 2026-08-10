import { useQuery } from '@tanstack/react-query';
import { Badge } from '../../components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/card';
import { Skeleton } from '../../components/ui/skeleton';
import { connectionsGateway, type MoodleConnection } from './connections-gateway';

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
  const query = useQuery({
    queryKey: ['portal', 'connections'],
    queryFn: connectionsGateway.list,
  });

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
