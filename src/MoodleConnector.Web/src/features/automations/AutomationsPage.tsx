import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Bot, Clock3, History, Pencil, Play, Plus, RefreshCw, Trash2, Workflow } from 'lucide-react';
import { useEffect, useMemo, useState, type FormEvent } from 'react';

import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from '@/components/ui/alert-dialog';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { useSession } from '../auth/useSession';
import { useConnectionScope } from '../connections/useConnectionScope';
import { automationsGateway, type Automation, type AutomationInput } from './automations-gateway';

const conditionLabels: Record<string, string> = {
  overdue_submissions: 'Atividades vencidas',
  awaiting_grading: 'Aguardando correção',
  weekly_signals: 'Sinais semanais de risco',
};

const actionLabels: Record<string, string> = {
  create_tasks: 'Criar tarefas',
  create_followups: 'Criar follow-ups',
  prepare_moodle_message: 'Preparar mensagem Moodle',
  create_followup_and_prepare_message: 'Follow-up + mensagem Moodle',
  generate_weekly_summary: 'Gerar resumo semanal',
};

const scheduleLabels: Record<string, string> = { manual: 'Somente manual', daily: 'Diária', weekly: 'Semanal' };
const weekDays = ['Domingo', 'Segunda-feira', 'Terça-feira', 'Quarta-feira', 'Quinta-feira', 'Sexta-feira', 'Sábado'];

const emptyForm: AutomationInput = {
  connectionAlias: undefined,
  courseId: '',
  name: '',
  description: '',
  scheduleType: 'manual',
  runHourUtc: 12,
  runMinuteUtc: 0,
  runDayOfWeek: 1,
  conditionType: 'overdue_submissions',
  actionType: 'create_tasks',
  config: { maxStudentsToAnalyze: 100, maxAssignmentsToAnalyze: 50 },
  isEnabled: true,
};

function formatDate(value?: string) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString('pt-BR');
}

function statusLabel(status: string) {
  if (status === 'succeeded') return 'Concluída';
  if (status === 'partial') return 'Parcial';
  if (status === 'failed') return 'Falhou';
  if (status === 'skipped') return 'Ignorada';
  return status;
}

function weeklySummaryLabel(summaryJson?: string) {
  if (!summaryJson) return undefined;
  try {
    const parsed = JSON.parse(summaryJson) as { weeklySummary?: { candidateCount?: number; urgentCount?: number; signals?: string[] } | null };
    const summary = parsed.weeklySummary;
    if (!summary) return undefined;
    const signals = summary.signals?.length ? ` · ${summary.signals.length} sinal(is)` : '';
    return `Resumo semanal: ${summary.candidateCount ?? 0} caso(s), ${summary.urgentCount ?? 0} urgente(s)${signals}`;
  } catch {
    return undefined;
  }
}

function AutomationCard({
  automation,
  onEdit,
  onDelete,
  onRun,
  onHistory,
  running,
  canManage,
}: {
  automation: Automation;
  onEdit: (automation: Automation) => void;
  onDelete: (automation: Automation) => void;
  onRun: (automation: Automation) => void;
  onHistory: (automation: Automation) => void;
  running: boolean;
  canManage: boolean;
}) {
  return (
    <Card className="card-interactive h-full">
      <CardHeader className="pb-3">
        <div className="flex items-start justify-between gap-3">
          <div className="flex min-w-0 items-start gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary"><Workflow className="h-5 w-5" /></div>
            <div className="min-w-0"><CardTitle className="truncate text-lg">{automation.name}</CardTitle><CardDescription className="mt-1">Curso Moodle {automation.courseId}</CardDescription></div>
          </div>
          <Badge variant={automation.isEnabled ? 'default' : 'outline'}>{automation.isEnabled ? 'Ativa' : 'Pausada'}</Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {automation.description && <p className="text-sm text-muted-foreground">{automation.description}</p>}
        <div className="grid gap-2 rounded-md border bg-muted/20 p-3 text-sm">
          <div className="flex items-center justify-between gap-3"><span className="text-muted-foreground">Quando</span><span className="font-medium">{scheduleLabels[automation.scheduleType] ?? automation.scheduleType}</span></div>
          <div className="flex items-center justify-between gap-3"><span className="text-muted-foreground">Condição</span><span className="text-right font-medium">{conditionLabels[automation.conditionType] ?? automation.conditionType}</span></div>
          <div className="flex items-center justify-between gap-3"><span className="text-muted-foreground">Ação</span><span className="text-right font-medium">{actionLabels[automation.actionType] ?? automation.actionType}</span></div>
        </div>
        <div className="flex items-center gap-2 text-xs text-muted-foreground"><Clock3 className="h-3.5 w-3.5" />Próxima execução: {automation.isEnabled ? formatDate(automation.nextRunAt) : 'desabilitada'}</div>
        {automation.actionType.includes('moodle_message') && <p className="rounded-md border border-status-pending/30 bg-status-pending/5 p-3 text-xs text-status-pending">A mensagem será apenas preparada. O envio depende de revisão e aprovação humana.</p>}
        <div className="flex flex-wrap gap-2 border-t pt-3">
          <Button type="button" size="sm" onClick={() => onRun(automation)} disabled={running || !canManage}><Play className="h-3.5 w-3.5" />{running ? 'Executando…' : 'Executar agora'}</Button>
          <Button type="button" variant="outline" size="sm" onClick={() => onHistory(automation)}><History className="h-3.5 w-3.5" />Histórico</Button>
          <Button type="button" variant="ghost" size="sm" onClick={() => onEdit(automation)} disabled={!canManage}><Pencil className="h-3.5 w-3.5" />Editar</Button>
          <Button type="button" variant="ghost" size="sm" className="text-muted-foreground hover:text-destructive" onClick={() => onDelete(automation)} disabled={!canManage}><Trash2 className="h-3.5 w-3.5" />Remover</Button>
        </div>
      </CardContent>
    </Card>
  );
}

export function AutomationsPage() {
  const client = useQueryClient();
  const { can } = useSession();
  const canManage = can('automations.manage');
  const { connectionRef, selectedConnection } = useConnectionScope();
  const query = useQuery({ queryKey: ['app', 'automations'], queryFn: automationsGateway.list, staleTime: 15_000 });
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<Automation>();
  const [form, setForm] = useState<AutomationInput>(emptyForm);
  const [removeTarget, setRemoveTarget] = useState<Automation>();
  const [historyTarget, setHistoryTarget] = useState<Automation>();
  const history = useQuery({ queryKey: ['app', 'automation-runs', historyTarget?.id], queryFn: () => automationsGateway.runs(historyTarget!.id), enabled: Boolean(historyTarget), staleTime: 5_000 });
  const [runningId, setRunningId] = useState<string>();

  const save = useMutation({
    mutationFn: (input: AutomationInput) => editing ? automationsGateway.update(editing.id, input) : automationsGateway.create(input),
    onSuccess: () => { setFormOpen(false); setEditing(undefined); void client.invalidateQueries({ queryKey: ['app', 'automations'] }); },
  });
  const remove = useMutation({
    mutationFn: (id: string) => automationsGateway.remove(id),
    onSuccess: () => { setRemoveTarget(undefined); void client.invalidateQueries({ queryKey: ['app', 'automations'] }); },
  });
  const run = useMutation({
    mutationFn: (id: string) => automationsGateway.run(id, true),
    onMutate: (id) => setRunningId(id),
    onSettled: (_data, _error, id) => { setRunningId(undefined); void client.invalidateQueries({ queryKey: ['app', 'automations'] }); void client.invalidateQueries({ queryKey: ['app', 'automation-runs', id] }); },
  });

  const automations = useMemo(() => query.data?.data ?? [], [query.data?.data]);

  useEffect(() => {
    if (!editing && connectionRef) setForm((current) => ({ ...current, connectionAlias: connectionRef }));
  }, [connectionRef, editing]);

  function updateField<K extends keyof AutomationInput>(key: K, value: AutomationInput[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function openCreate() {
    setEditing(undefined);
    setForm({ ...emptyForm, connectionAlias: connectionRef });
    setFormOpen(true);
  }

  function openEdit(item: Automation) {
    setEditing(item);
    setForm({
      connectionAlias: item.connectionAlias,
      courseId: item.courseId,
      name: item.name,
      description: item.description ?? '',
      scheduleType: item.scheduleType === 'daily' || item.scheduleType === 'weekly' ? item.scheduleType : 'manual',
      runHourUtc: item.runHourUtc,
      runMinuteUtc: item.runMinuteUtc,
      runDayOfWeek: item.runDayOfWeek ?? 1,
      conditionType: item.conditionType,
      actionType: item.actionType,
      config: item.config,
      isEnabled: item.isEnabled,
    });
    setFormOpen(true);
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!form.name.trim() || !form.courseId.trim()) return;
    save.mutate({ ...form, name: form.name.trim(), courseId: form.courseId.trim(), description: form.description?.trim() || undefined, connectionAlias: form.connectionAlias || connectionRef });
  }

  return (
    <main className="content-frame" aria-labelledby="automations-title">
      <header className="page-heading"><div><p className="eyebrow">OPERACIONAL · MOODLE-FIRST</p><h1 id="automations-title">Automações</h1><p>Transforme sinais acadêmicos do Moodle em tarefas, follow-ups e mensagens preparadas para aprovação.</p></div><div className="flex flex-wrap items-center gap-2"><span className="freshness">Escopo: {selectedConnection?.alias ?? connectionRef ?? 'Moodle padrão'}</span><Button type="button" onClick={openCreate} disabled={!canManage}><Plus />Nova automação</Button></div></header>
      <Card><CardHeader><CardTitle className="flex items-center gap-2 text-lg"><Bot className="h-5 w-5 text-primary" />Rotinas internas do Connector</CardTitle><CardDescription>O scheduler roda dentro do .NET e registra cada execução, ação, retry e idempotência no PostgreSQL.</CardDescription></CardHeader><CardContent className="grid gap-3 md:grid-cols-3"><div className="rounded-lg border bg-muted/20 p-4"><p className="font-medium">Moodle como fonte</p><p className="mt-1 text-xs text-muted-foreground">Condições são avaliadas com pendências, correções e sinais acadêmicos do Moodle.</p></div><div className="rounded-lg border bg-muted/20 p-4"><p className="font-medium">Ações locais auditáveis</p><p className="mt-1 text-xs text-muted-foreground">Tarefas e follow-ups aparecem no portal com histórico operacional.</p></div><div className="rounded-lg border bg-muted/20 p-4"><p className="font-medium">Aprovação humana</p><p className="mt-1 text-xs text-muted-foreground">Mensagens Moodle nunca são enviadas automaticamente por esta rotina.</p></div></CardContent></Card>
      <div className="flex items-center justify-between gap-3"><div><h2 className="text-lg font-semibold">Rotinas cadastradas</h2><p className="text-sm text-muted-foreground">{automations.length} automação{automations.length === 1 ? '' : 'ões'} no escopo da conta.</p></div><Button type="button" variant="outline" size="sm" onClick={() => void query.refetch()} disabled={query.isFetching}><RefreshCw className={query.isFetching ? 'animate-spin' : ''} />Atualizar</Button></div>
      {query.isError && <p className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive" role="alert">Não foi possível carregar as automações.</p>}
      {save.isError && <p className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive" role="alert">{save.error instanceof Error ? save.error.message : 'Não foi possível salvar a automação.'}</p>}
      {run.isError && <p className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive" role="alert">{run.error instanceof Error ? run.error.message : 'A execução falhou.'}</p>}
      {query.isPending && <p className="text-sm text-muted-foreground">Carregando automações…</p>}
      {query.isSuccess && automations.length === 0 && <Card className="border-dashed"><CardContent className="flex flex-col items-center gap-3 p-12 text-center"><Workflow className="h-10 w-10 text-muted-foreground/40" /><h2 className="font-medium">Nenhuma automação cadastrada</h2><p className="text-sm text-muted-foreground">Comece criando uma rotina para acompanhar pendências acadêmicas no Moodle.</p><Button type="button" variant="outline" onClick={openCreate}><Plus />Criar primeira automação</Button></CardContent></Card>}
      {automations.length > 0 && <section className="grid gap-4 lg:grid-cols-2" aria-label="Automações cadastradas">{automations.map((item) => <AutomationCard key={item.id} automation={item} onEdit={openEdit} onDelete={setRemoveTarget} onRun={(automation) => run.mutate(automation.id)} onHistory={setHistoryTarget} running={runningId === item.id} canManage={canManage} />)}</section>}

      <Dialog open={formOpen} onOpenChange={(open) => { if (!open) { setFormOpen(false); setEditing(undefined); } }}><DialogContent className="sm:max-w-2xl"><DialogHeader><DialogTitle>{editing ? 'Editar automação' : 'Nova automação'}</DialogTitle><DialogDescription>Defina uma condição observável no Moodle e a ação que o Connector deve registrar.</DialogDescription></DialogHeader><form className="grid gap-4" onSubmit={submit}><div className="grid gap-4 sm:grid-cols-2"><label className="grid gap-1.5 text-sm font-medium">Nome<Input value={form.name} onChange={(event) => updateField('name', event.target.value)} placeholder="Ex.: Atividades vencidas" required /></label><label className="grid gap-1.5 text-sm font-medium">Curso Moodle<Input value={form.courseId} onChange={(event) => updateField('courseId', event.target.value)} placeholder="ID do curso" required /></label></div><label className="grid gap-1.5 text-sm font-medium">Descrição <span className="font-normal text-muted-foreground">opcional</span><Textarea value={form.description ?? ''} onChange={(event) => updateField('description', event.target.value)} placeholder="Qual resultado operacional esta rotina deve produzir?" /></label><div className="grid gap-4 sm:grid-cols-2"><label className="grid gap-1.5 text-sm font-medium">Condição<Select value={form.conditionType} onValueChange={(value) => updateField('conditionType', value)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="overdue_submissions">Atividades vencidas</SelectItem><SelectItem value="awaiting_grading">Aguardando correção</SelectItem><SelectItem value="weekly_signals">Sinais semanais de risco</SelectItem></SelectContent></Select></label><label className="grid gap-1.5 text-sm font-medium">Ação<Select value={form.actionType} onValueChange={(value) => updateField('actionType', value)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="create_tasks">Criar tarefas</SelectItem><SelectItem value="create_followups">Criar follow-ups</SelectItem><SelectItem value="prepare_moodle_message">Preparar mensagem Moodle</SelectItem><SelectItem value="create_followup_and_prepare_message">Follow-up + mensagem Moodle</SelectItem><SelectItem value="generate_weekly_summary">Gerar resumo semanal</SelectItem></SelectContent></Select></label></div><div className="grid gap-4 sm:grid-cols-3"><label className="grid gap-1.5 text-sm font-medium">Frequência<Select value={form.scheduleType} onValueChange={(value) => updateField('scheduleType', value as AutomationInput['scheduleType'])}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="manual">Somente manual</SelectItem><SelectItem value="daily">Diária</SelectItem><SelectItem value="weekly">Semanal</SelectItem></SelectContent></Select></label><label className="grid gap-1.5 text-sm font-medium">Hora UTC<Input type="number" min="0" max="23" value={form.runHourUtc} onChange={(event) => updateField('runHourUtc', Number(event.target.value))} disabled={form.scheduleType === 'manual'} /></label><label className="grid gap-1.5 text-sm font-medium">Minuto<Input type="number" min="0" max="59" value={form.runMinuteUtc} onChange={(event) => updateField('runMinuteUtc', Number(event.target.value))} disabled={form.scheduleType === 'manual'} /></label></div>{form.scheduleType === 'weekly' && <label className="grid gap-1.5 text-sm font-medium">Dia da semana<Select value={String(form.runDayOfWeek ?? 1)} onValueChange={(value) => updateField('runDayOfWeek', Number(value))}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{weekDays.map((day, index) => <SelectItem key={day} value={String(index)}>{day}</SelectItem>)}</SelectContent></Select></label>}{(form.actionType.includes('moodle_message')) && <label className="grid gap-1.5 text-sm font-medium">Mensagem a preparar<Textarea value={form.config?.messageText ?? ''} onChange={(event) => setForm((current) => ({ ...current, config: { ...current.config, messageText: event.target.value } }))} placeholder="Texto que ficará no preview para revisão humana" required /></label>}<label className="flex items-center gap-2 rounded-md border p-3 text-sm"><input type="checkbox" checked={form.isEnabled} onChange={(event) => updateField('isEnabled', event.target.checked)} />Ativar scheduler para esta rotina</label>{save.isError && <p className="text-sm text-destructive" role="alert">Não foi possível salvar a automação.</p>}<DialogFooter><Button type="button" variant="outline" onClick={() => setFormOpen(false)}>Cancelar</Button><Button type="submit" disabled={save.isPending || !form.name.trim() || !form.courseId.trim()}>{save.isPending ? 'Salvando…' : 'Salvar automação'}</Button></DialogFooter></form></DialogContent></Dialog>
      <AlertDialog open={Boolean(removeTarget)} onOpenChange={(open) => { if (!open) setRemoveTarget(undefined); }}><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>Remover automação?</AlertDialogTitle><AlertDialogDescription>A definição será removida. O histórico de execuções e auditoria permanece preservado.</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel>Cancelar</AlertDialogCancel><AlertDialogAction className="bg-destructive text-destructive-foreground hover:bg-destructive/90" onClick={() => removeTarget && remove.mutate(removeTarget.id)}>{remove.isPending ? 'Removendo…' : 'Remover'}</AlertDialogAction></AlertDialogFooter></AlertDialogContent></AlertDialog>
      <Dialog open={Boolean(historyTarget)} onOpenChange={(open) => { if (!open) setHistoryTarget(undefined); }}><DialogContent className="sm:max-w-2xl"><DialogHeader><DialogTitle>Histórico · {historyTarget?.name}</DialogTitle><DialogDescription>Execuções e ações registradas para esta rotina.</DialogDescription></DialogHeader>{history.isPending && <p className="text-sm text-muted-foreground">Carregando histórico…</p>}{history.isError && <p className="text-sm text-destructive" role="alert">Não foi possível carregar o histórico.</p>}{history.data?.data.length === 0 && <p className="text-sm text-muted-foreground">Ainda não há execuções registradas.</p>}{history.data && history.data.data.length > 0 && <div className="max-h-[55vh] space-y-2 overflow-y-auto">{history.data.data.map((item) => <div key={item.runId} className="rounded-md border p-3 text-sm"><div className="flex flex-wrap items-center justify-between gap-2"><div className="flex items-center gap-2"><Badge variant={item.status === 'succeeded' ? 'default' : item.status === 'failed' ? 'destructive' : 'outline'}>{statusLabel(item.status)}</Badge><span className="text-muted-foreground">{item.trigger}</span></div><span className="text-xs text-muted-foreground">{formatDate(item.finishedAt ?? item.startedAt)}</span></div><p className="mt-2 text-xs text-muted-foreground">Ações criadas: {item.createdActions} · ignoradas: {item.skippedActions} · falhas: {item.failedActions}{item.pendingActionIds.length > 0 ? ` · ${item.pendingActionIds.length} pendente(s) de aprovação` : ''}</p>{item.errorMessage && <p className="mt-2 text-xs text-destructive">{item.errorMessage}</p>}</div>)}</div>}<DialogFooter><Button type="button" variant="outline" onClick={() => setHistoryTarget(undefined)}>Fechar</Button></DialogFooter></DialogContent></Dialog>
      {history.data && history.data.data.some((item) => weeklySummaryLabel(item.summaryJson)) && <Card><CardHeader><CardTitle className="text-base">Resumo semanal registrado</CardTitle><CardDescription>Resumo produzido pelo runtime interno a partir de sinais observáveis do Moodle.</CardDescription></CardHeader><CardContent className="space-y-2">{history.data.data.filter((item) => weeklySummaryLabel(item.summaryJson)).map((item) => <p key={`${item.runId}-summary`} className="rounded-md border border-primary/20 bg-primary/5 p-2 text-sm text-primary">{weeklySummaryLabel(item.summaryJson)}</p>)}</CardContent></Card>}
    </main>
  );
}
