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
  unknown: 'NÃ£o testada',
};

function formatFreshness(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'AtualizaÃ§Ã£o indisponÃ­vel';
  return `Atualizado em ${new Intl.DateTimeFormat('pt-BR', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)}`;
}

function ConnectionCard({ connection, onValidate, onEdit, onRemove, validating }: { connection: MoodleConnection; onValidate: (connectionId: string) => void; onEdit: (connection: MoodleConnection) => void; onRemove: (connection: MoodleConnection) => void; validating: boolean }) {
  return (
    <Card className="connection-card">
      <CardHeader>
        <div className="connection-card-heading">
          <div>
            <CardTitle>{connection.alias}</CardTitle>
            <span className="connection-ref">{connection.connectionRef}</span>
          </div>
          <Badge variant={connection.status === 'active' ? 'default' : 'outline'}>{statusLabels[connection.status] ?? connection.status}</Badge>
        </div>
      </CardHeader>
      <CardContent>
        <p className="connection-host">{connection.host}</p>
        <div className="connection-details">
          {connection.isDefault && <Badge> padrÃ£o</Badge>}
          {connection.capabilities.map((capability) => (
            <Badge key={`${connection.connectionRef}-${capability}`}>{capability}</Badge>
          ))}
        </div>
        <p className="connection-validation">{connection.lastValidatedAt ? `Ãšltimo teste em ${formatFreshness(connection.lastValidatedAt).replace('Atualizado em ', '')}` : 'Ainda nÃ£o testada nesta versÃ£o do app'}</p>
        <div className="connection-actions">
        <button type="button" className="connection-test" onClick={() => onValidate(connection.connectionId ?? connection.connectionRef)} disabled={validating}>
          {validating ? 'Testandoâ€¦' : 'Testar conexÃ£o'}
        </button>
        <button type="button" className="connection-action-secondary" onClick={() => onEdit(connection)}>Editar</button>
        <button type="button" className="connection-action-danger" onClick={() => onRemove(connection)}>Remover</button>
        </div>
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
  const [validationError, setValidationError] = useState('');
  const [validatingRef, setValidatingRef] = useState<string>();
  const [editingConnection, setEditingConnection] = useState<MoodleConnection>();
  const [removeTarget, setRemoveTarget] = useState<MoodleConnection>();
  const [removeSummary, setRemoveSummary] = useState<{ memories: number; documents: number; moodleUserLinks: number; auditLogsRetained: number }>();
  const [deleteLinkedData, setDeleteLinkedData] = useState(false);
  const [confirmationText, setConfirmationText] = useState('');
  const query = useQuery({
    queryKey: ['app', 'connections'],
    queryFn: connectionsGateway.list,
  });
  const mutation = useMutation({ mutationFn: connectionsGateway.connect, onSuccess: () => { setSuccess('ConexÃ£o cadastrada com sucesso.'); setAlias(''); setBaseUrl(''); setUsername(''); setPassword(''); void queryClient.invalidateQueries({ queryKey: ['app', 'connections'] }); } });
  const updateMutation = useMutation({ mutationFn: ({ id, input }: { id: string; input: Parameters<typeof connectionsGateway.update>[1] }) => connectionsGateway.update(id, input), onSuccess: () => { setSuccess('ConexÃ£o atualizada com sucesso.'); setEditingConnection(undefined); setAlias(''); setBaseUrl(''); setUsername(''); setPassword(''); void queryClient.invalidateQueries({ queryKey: ['app', 'connections'] }); } });
  const validate = async (connectionRef: string) => {
    setValidatingRef(connectionRef);
    setValidationError('');
    try { await connectionsGateway.validate(connectionRef); await queryClient.invalidateQueries({ queryKey: ['app', 'connections'] }); }
    catch (error) { setValidationError(error instanceof Error ? error.message : 'NÃ£o foi possÃ­vel testar a conexÃ£o.'); }
    finally { setValidatingRef(undefined); }
  };
  const startEdit = (connection: MoodleConnection) => { setEditingConnection(connection); setAlias(connection.alias); setBaseUrl(connection.host); setUsername(''); setPassword(''); setSuccess(''); };
  const getConnectionId = (connection: MoodleConnection) => connection.connectionId ?? connection.connectionRef;
  const openRemove = async (connection: MoodleConnection) => { setRemoveTarget(connection); setDeleteLinkedData(false); setConfirmationText(''); setRemoveSummary(undefined); try { setRemoveSummary(await connectionsGateway.dataSummary(getConnectionId(connection))); } catch (error) { setValidationError(error instanceof Error ? error.message : 'NÃ£o foi possÃ­vel consultar os dados associados.'); } };
  const confirmRemove = async () => { if (!removeTarget) return; setValidationError(''); try { await connectionsGateway.remove(getConnectionId(removeTarget), deleteLinkedData, confirmationText || undefined); setRemoveTarget(undefined); await queryClient.invalidateQueries({ queryKey: ['app', 'connections'] }); } catch (error) { setValidationError(error instanceof Error ? error.message : 'NÃ£o foi possÃ­vel remover a conexÃ£o.'); } };

  return (
    <main className="content-frame connections-page">
      <header className="page-heading">
        <div>
          <p className="eyebrow">GESTÃƒO</p>
          <h1>ConexÃµes Moodle</h1>
          <p>Consulte os Moodles disponÃ­veis e o estado de cada conexÃ£o.</p>
        </div>
        {query.data?.meta.generatedAt && <span className="freshness">{formatFreshness(query.data.meta.generatedAt)}</span>}
      </header>

      <Card className="connection-management"><CardHeader><CardTitle>{editingConnection ? 'Editar conexÃ£o Moodle' : 'Adicionar conexÃ£o Moodle'}</CardTitle><p>{editingConnection ? 'Atualize o nome, URL ou permissÃµes. Uma nova URL serÃ¡ validada antes de salvar.' : 'Cadastre o acesso do Moodle que serÃ¡ usado pelo app.'}</p></CardHeader><CardContent><form className="connection-form" onSubmit={event => { event.preventDefault(); setSuccess(''); const input = { moodleAlias: alias, moodleBaseUrl: baseUrl, moodleUsername: username || undefined, moodlePassword: password || undefined, isDefault, canWrite }; if (editingConnection) updateMutation.mutate({ id: getConnectionId(editingConnection), input }); else mutation.mutate({ moodleAlias: alias, moodleBaseUrl: baseUrl, moodleUsername: username, moodlePassword: password, isDefault, canWrite }); }}><div className="form-grid"><label>Nome da conexÃ£o<input required value={alias} onChange={event => setAlias(event.target.value)} /></label><label>URL base do Moodle<input required type="url" placeholder="https://moodle.exemplo.com" value={baseUrl} onChange={event => setBaseUrl(event.target.value)} /></label><label>UsuÃ¡rio Moodle<input required={!editingConnection} value={username} onChange={event => setUsername(event.target.value)} placeholder={editingConnection ? 'Deixe vazio para manter' : undefined} /></label><label>Senha Moodle<input required={!editingConnection} type="password" value={password} onChange={event => setPassword(event.target.value)} placeholder={editingConnection ? 'Deixe vazio para manter' : undefined} /></label></div><div className="form-checks"><label><input type="checkbox" checked={isDefault} onChange={event => setIsDefault(event.target.checked)} /> Usar como padrÃ£o</label><label><input type="checkbox" checked={canWrite} onChange={event => setCanWrite(event.target.checked)} /> Permitir operaÃ§Ãµes de escrita</label></div>{(mutation.isError || updateMutation.isError) && <p className="auth-error" role="alert">{(mutation.error ?? updateMutation.error) instanceof Error ? (mutation.error ?? updateMutation.error)?.message : 'NÃ£o foi possÃ­vel salvar a conexÃ£o.'}</p>}<div className="form-actions"><button className="auth-submit" type="submit" disabled={mutation.isPending || updateMutation.isPending}>{mutation.isPending || updateMutation.isPending ? 'Validandoâ€¦' : editingConnection ? 'Salvar alteraÃ§Ãµes' : 'Cadastrar conexÃ£o'}</button>{editingConnection && <button type="button" className="connection-action-secondary" onClick={() => { setEditingConnection(undefined); setAlias(''); setBaseUrl(''); setUsername(''); setPassword(''); }}>Cancelar</button>}{success && <p className="form-success" role="status">{success}</p>}</div></form></CardContent></Card>

      {validationError && <p className="auth-error" role="alert">{validationError}</p>}

      {query.isPending && (
        <div className="connections-grid" aria-label="Carregando conexÃµes">
          <Skeleton className="connection-skeleton" />
          <Skeleton className="connection-skeleton" />
        </div>
      )}

      {query.isError && (
        <Card><CardContent><p role="alert">NÃ£o foi possÃ­vel carregar as conexÃµes Moodle.</p></CardContent></Card>
      )}

      {query.isSuccess && query.data.data.length === 0 && (
        <Card><CardContent><p>Nenhuma conexÃ£o Moodle disponÃ­vel.</p></CardContent></Card>
      )}

      {query.isSuccess && query.data.data.length > 0 && (
        <section className="connections-grid" aria-label="ConexÃµes Moodle">
          {query.data.data.map((connection) => <ConnectionCard key={getConnectionId(connection)} connection={connection} onValidate={validate} onEdit={startEdit} onRemove={openRemove} validating={validatingRef === getConnectionId(connection)} />)}
        </section>
      )}

      {removeTarget && <div className="connection-dialog-backdrop" role="presentation"><section className="connection-dialog" role="dialog" aria-modal="true" aria-labelledby="remove-connection-title"><h2 id="remove-connection-title">Remover {removeTarget.alias}?</h2><p>A conexÃ£o serÃ¡ removida do app. Escolha o que fazer com os dados associados.</p>{removeSummary ? <div className="connection-summary"><p><strong>{removeSummary.memories + removeSummary.documents + removeSummary.moodleUserLinks}</strong> registros de contexto serÃ£o afetados.</p><p><strong>{removeSummary.auditLogsRetained}</strong> registros de auditoria serÃ£o preservados.</p></div> : <p>Consultando dados associadosâ€¦</p>}<label className="connection-radio"><input type="radio" name="remove-policy" checked={!deleteLinkedData} onChange={() => { setDeleteLinkedData(false); setConfirmationText(''); }} /> Remover somente a conexÃ£o</label><label className="connection-radio"><input type="radio" name="remove-policy" checked={deleteLinkedData} onChange={() => setDeleteLinkedData(true)} /> Remover a conexÃ£o e os dados associados</label>{deleteLinkedData && <label className="connection-confirm">Digite <strong>EXCLUIR CONEXÃƒO E DADOS</strong><input value={confirmationText} onChange={event => setConfirmationText(event.target.value)} /></label>}<div className="connection-dialog-actions"><button type="button" className="connection-action-secondary" onClick={() => setRemoveTarget(undefined)}>Cancelar</button><button type="button" className="connection-action-danger" disabled={deleteLinkedData && confirmationText.trim() !== 'EXCLUIR CONEXÃƒO E DADOS'} onClick={() => void confirmRemove()}>Remover</button></div></section></div>}
    </main>
  );
}

