import { FormEvent, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { AlertCircle, BarChart3, ClipboardCheck, FileClock, RefreshCw, UsersRound } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { StatCard } from '@/components/ui/StatCard';
import { connectionDisplayName, useConnectionScope } from '../connections/useConnectionScope';
import { reportsGateway } from './reports-gateway';

function MetricRow({ label, value, tone = 'default' }: { label: string; value: number; tone?: 'default' | 'warning' | 'danger' | 'success' }) {
  const toneClass = tone === 'danger' ? 'text-risk-critico' : tone === 'warning' ? 'text-status-warning' : tone === 'success' ? 'text-status-success' : 'text-foreground';
  return <div className="flex items-center justify-between gap-4 border-b py-3 last:border-0"><span className="text-sm text-muted-foreground">{label}</span><span className={`text-lg font-semibold ${toneClass}`}>{value}</span></div>;
}

export function ReportsPage() {
  const { connectionRef, selectedConnection } = useConnectionScope();
  const operational = useQuery({ queryKey: ['app', 'reports', 'operational'], queryFn: reportsGateway.operational, staleTime: 30_000 });
  const audit = useQuery({ queryKey: ['app', 'reports', 'audit'], queryFn: reportsGateway.audit, staleTime: 30_000 });
  const [connectionInput, setConnectionInput] = useState(connectionRef ?? '');
  const [courseId, setCourseId] = useState('');
  const [scope, setScope] = useState<{ connectionRef: string; courseId: string }>();
  const run = (event: FormEvent) => { event.preventDefault(); if (connectionInput.trim() && courseId.trim()) setScope({ connectionRef: connectionInput.trim(), courseId: courseId.trim() }); };
  const overview = useQuery({ queryKey: ['app', 'reports', 'overview', scope], queryFn: () => reportsGateway.courseOverview(scope!.connectionRef, scope!.courseId), enabled: Boolean(scope), staleTime: 60_000 });
  const weekly = useQuery({ queryKey: ['app', 'reports', 'weekly', scope], queryFn: () => reportsGateway.weekly(scope!.connectionRef, scope!.courseId), enabled: Boolean(scope), staleTime: 60_000 });
  const completion = useQuery({ queryKey: ['app', 'reports', 'completion', scope], queryFn: () => reportsGateway.completion(scope!.connectionRef, scope!.courseId), enabled: Boolean(scope), staleTime: 60_000 });
  const operationalData = operational.data?.data;
  const auditData = audit.data?.data;
  const scopeError = overview.isError || weekly.isError || completion.isError;

  return (
    <main className="space-y-6 animate-fade-in" aria-labelledby="reports-title">
      <header className="page-heading"><div><p className="eyebrow">GESTÃO</p><h1 id="reports-title">Relatórios</h1><p>Indicadores objetivos para acompanhar a operação e investigar cursos específicos.</p></div><div className="flex items-center gap-3">{operational.data && <span className="freshness">Atualizado em {new Date(operational.data.meta.generatedAt).toLocaleString('pt-BR')}</span>}<Button type="button" variant="outline" onClick={() => { void operational.refetch(); void audit.refetch(); }} disabled={operational.isFetching || audit.isFetching}><RefreshCw className={(operational.isFetching || audit.isFetching) ? 'animate-spin' : ''} />Atualizar</Button></div></header>

      {operational.isPending && <section className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4" aria-label="Carregando indicadores">{[1, 2, 3, 4].map((item) => <Skeleton key={item} className="h-32 rounded-lg" />)}</section>}
      {operational.isError && <Card className="border-destructive/30"><CardContent className="flex items-center gap-2 p-6 text-sm text-destructive" role="alert"><AlertCircle className="h-4 w-4" />Não foi possível carregar os indicadores operacionais.</CardContent></Card>}
      {operationalData && <section aria-labelledby="operational-title" className="space-y-4"><div className="flex items-center justify-between"><h2 id="operational-title" className="text-lg font-semibold">Visão operacional</h2><Badge variant="outline">{connectionDisplayName(selectedConnection)}</Badge></div><div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4"><StatCard title="Tarefas abertas" value={operationalData.openTasks} subtitle="na fila de trabalho" icon={ClipboardCheck} variant="warning" /><StatCard title="Tarefas concluídas" value={operationalData.completedTasks} subtitle="finalizadas" icon={ClipboardCheck} variant="success" /><StatCard title="Próximos eventos" value={operationalData.upcomingEvents} subtitle="na agenda" icon={FileClock} variant="pending" /><StatCard title="Follow-ups" value={operationalData.followupsRecorded} subtitle="registrados" icon={UsersRound} /></div></section>}

      <section className="grid gap-6 lg:grid-cols-[minmax(0,1.2fr)_minmax(300px,0.8fr)]">
        <Card><CardHeader><div className="flex items-center gap-3"><div className="flex h-10 w-10 items-center justify-center rounded-full bg-primary/10 text-primary"><BarChart3 className="h-5 w-5" /></div><div><CardTitle className="text-lg">Relatórios acadêmicos</CardTitle><CardDescription>Gere leituras sob demanda para um curso específico.</CardDescription></div></div></CardHeader><CardContent><form onSubmit={run} className="grid gap-4 sm:grid-cols-2"><label className="grid gap-1.5 text-sm font-medium">Conexão Moodle<Input value={connectionInput} onChange={(event) => setConnectionInput(event.target.value)} placeholder="Ex.: fieg" required /></label><label className="grid gap-1.5 text-sm font-medium">ID do curso<Input value={courseId} onChange={(event) => setCourseId(event.target.value)} placeholder="Ex.: 123" required /></label><div className="flex items-center justify-between gap-3 sm:col-span-2"><p className="text-xs text-muted-foreground">A consulta usa o escopo selecionado e permanece somente leitura.</p><Button type="submit" disabled={!connectionInput.trim() || !courseId.trim()}>Gerar relatórios</Button></div></form>{scope && <div className="mt-5 flex items-center justify-between gap-3 rounded-md border bg-muted/20 p-3 text-sm"><span className="text-muted-foreground">Escopo consultado</span><span className="font-medium">{scope.connectionRef} · curso {scope.courseId}</span></div>}{scopeError && <p className="mt-4 flex items-center gap-2 text-sm text-destructive" role="alert"><AlertCircle className="h-4 w-4" />Não foi possível atualizar um dos relatórios acadêmicos.</p>}</CardContent></Card>
        <Card><CardHeader><CardTitle className="text-lg">Auditoria de ações</CardTitle><CardDescription>Registro resumido das ações realizadas no portal.</CardDescription></CardHeader><CardContent>{audit.isPending && <p className="text-sm text-muted-foreground">Carregando auditoria…</p>}{audit.isError && <p className="text-sm text-destructive" role="alert">Não foi possível carregar a auditoria.</p>}{auditData && <div><MetricRow label="Ações registradas" value={auditData.totalActions} /><MetricRow label="Concluídas" value={auditData.completedActions} tone="success" /><MetricRow label="Confirmações humanas" value={auditData.confirmedActions} /><MetricRow label="Falhas" value={auditData.failedActions} tone="danger" /></div>}</CardContent></Card>
      </section>

      {scope && <section className="space-y-4" aria-labelledby="academic-results-title"><div className="flex items-center justify-between"><div><h2 id="academic-results-title" className="text-lg font-semibold">Leitura acadêmica</h2><p className="text-sm text-muted-foreground">Resultados para {scope.connectionRef} · curso {scope.courseId}</p></div>{(overview.isFetching || weekly.isFetching || completion.isFetching) && <span className="text-xs text-muted-foreground">Atualizando…</span>}</div><div className="grid gap-4 md:grid-cols-3"><Card><CardHeader className="pb-3"><CardTitle className="text-base">Visão geral</CardTitle></CardHeader><CardContent>{overview.data ? <><MetricRow label="Alunos ativos" value={overview.data.data.totalActiveStudents} tone="success" /><MetricRow label="Inativos" value={overview.data.data.studentsInactiveDays} tone="warning" /></> : <p className="text-sm text-muted-foreground">Carregando…</p>}</CardContent></Card><Card><CardHeader className="pb-3"><CardTitle className="text-base">Atenção e risco</CardTitle></CardHeader><CardContent>{weekly.data ? <><MetricRow label="Em atenção" value={weekly.data.data.studentsWithAttention} tone="warning" /><MetricRow label="Em risco" value={weekly.data.data.studentsAtRisk} tone="danger" /></> : <p className="text-sm text-muted-foreground">Carregando…</p>}</CardContent></Card><Card><CardHeader className="pb-3"><CardTitle className="text-base">Conclusão provável</CardTitle></CardHeader><CardContent>{completion.data ? <><MetricRow label="Prováveis concluídos" value={completion.data.data.likelyComplete} tone="success" /><MetricRow label="Em recuperação" value={completion.data.data.pendingRecovery} tone="warning" /></> : <p className="text-sm text-muted-foreground">Carregando…</p>}</CardContent></Card></div></section>}
    </main>
  );
}
