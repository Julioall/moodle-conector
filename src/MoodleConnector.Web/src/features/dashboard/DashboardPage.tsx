import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo } from 'react';
import { AlertCircle, AlertTriangle, BookOpen, CalendarDays, CheckSquare, ClipboardCheck, RefreshCw, UserCheck, UsersRound } from 'lucide-react';
import { Link } from 'react-router-dom';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { ScrollArea } from '@/components/ui/scroll-area';
import { Skeleton } from '@/components/ui/skeleton';
import { StatCard } from '@/components/ui/StatCard';
import { useConnectionScope } from '../connections/useConnectionScope';
import { dashboardGateway, type DashboardAccessMetric, type DashboardPendingMetric, type DashboardPriority, type DashboardSummaryMetric } from './dashboard-gateway';

const DASHBOARD_STALE_TIME = 5 * 60_000;
const DASHBOARD_GC_TIME = 15 * 60_000;

function ScopeLink({ connectionRef, courseId, studentId, children }: { connectionRef?: string; courseId?: string; studentId?: string; children: React.ReactNode }) {
  if (!connectionRef || !courseId || !studentId) return <>{children}</>;
  return <Link className="hover:underline" to={`/cursos/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}/alunos/${encodeURIComponent(studentId)}`}>{children}</Link>;
}

function formatTime(value?: string) {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
}

function formatSnapshotDate(value: string) {
  const date = new Date(`${value}T12:00:00`);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' });
}

function metricValue(value?: number | null, loading = false, error = false) {
  return loading ? '…' : error ? '—' : value ?? 0;
}

export function DashboardPage() {
  const { connectionRef } = useConnectionScope();
  const queryClient = useQueryClient();

  const summaryQuery = useQuery<DashboardSummaryMetric>({
    queryKey: ['app', 'dashboard', 'summary', connectionRef],
    queryFn: () => dashboardGateway.getMetric<DashboardSummaryMetric>('summary', connectionRef),
    enabled: Boolean(connectionRef),
    staleTime: DASHBOARD_STALE_TIME,
    gcTime: DASHBOARD_GC_TIME,
  });
  const pendingQuery = useQuery<DashboardPendingMetric>({
    queryKey: ['app', 'dashboard', 'pending', connectionRef],
    queryFn: () => dashboardGateway.getMetric<DashboardPendingMetric>('pending', connectionRef),
    enabled: Boolean(connectionRef),
    staleTime: DASHBOARD_STALE_TIME,
    gcTime: DASHBOARD_GC_TIME,
    refetchInterval: (query) => query.state.data?.data.isRefreshing ? 10_000 : false,
  });
  const accessQuery = useQuery<DashboardAccessMetric>({
    queryKey: ['app', 'dashboard', 'access', connectionRef],
    queryFn: () => dashboardGateway.getMetric<DashboardAccessMetric>('access', connectionRef),
    enabled: Boolean(connectionRef),
    staleTime: DASHBOARD_STALE_TIME,
    gcTime: DASHBOARD_GC_TIME,
  });
  const refresh = (metric: 'summary' | 'pending' | 'access') => {
    if (!connectionRef) return;
    void queryClient.fetchQuery({
      queryKey: ['app', 'dashboard', metric, connectionRef],
      queryFn: () => dashboardGateway.getMetric(metric, connectionRef, true),
      staleTime: 0,
    });
  };
  const refreshAll = () => {
    refresh('summary');
    refresh('pending');
    refresh('access');
  };
  const priorityItems = useMemo(() => {
    const source = [...(pendingQuery.data?.data.activitiesToReview ?? []), ...(pendingQuery.data?.data.priorities ?? [])];
    return Array.from(new Map(source.map((item) => [item.key, item])).values());
  }, [pendingQuery.data]);

  const summary = { ...(summaryQuery.data?.data.summary ?? {}), ...(pendingQuery.data?.data.summary ?? {}), ...(accessQuery.data?.data.summary ?? {}) };
  const warnings = [...new Set([
    ...(summaryQuery.data?.data.warnings ?? []),
    ...(pendingQuery.data?.data.warnings ?? []),
    ...(accessQuery.data?.data.warnings ?? []),
  ])];
  const metricErrors = [summaryQuery, pendingQuery, accessQuery].filter((query) => query.isError).length;
  const notices = [...warnings, ...(metricErrors > 0 ? [`${metricErrors} métrica${metricErrors === 1 ? '' : 's'} não pôde${metricErrors === 1 ? '' : 'ram'} ser carregada${metricErrors === 1 ? '' : 's'}.`] : [])];
  const isSummaryLoading = summaryQuery.isPending;
  const isPendingLoading = pendingQuery.isPending;
  const isPendingRefreshing = pendingQuery.data?.data.isRefreshing ?? false;
  const isAccessLoading = accessQuery.isPending;
  const todayItems = pendingQuery.data?.data.todayItems ?? [];
  const coursePendingSummaries = pendingQuery.data?.data.courseSummaries ?? [];
  const analyzedCorrectionDeliveries = coursePendingSummaries.reduce((total, course) => total + course.pendingCorrectionSubmissions, 0);
  const correctionCount = Math.max(analyzedCorrectionDeliveries, summary.pendingCorrectionAssignments ?? 0, pendingQuery.data?.data.activitiesToReview.length ?? 0);
  const accessSegments = accessQuery.data?.data.segments ?? [];
  const accessSnapshots = accessQuery.data?.data.snapshots ?? [];
  const snapshotMax = Math.max(1, ...accessSnapshots.map((snapshot) => snapshot.totalStudents));
  const neverAccessedCount = accessSegments.find((segment) => segment.key === 'never')?.students ?? summary.neverAccessedStudents ?? 0;

  return (
    <main className="space-y-6 animate-fade-in" aria-labelledby="dashboard-title">
      <header className="flex flex-col gap-2 md:flex-row md:items-end md:justify-between">
        <div><p className="eyebrow">MONITORAMENTO</p><h1 id="dashboard-title" className="text-2xl font-bold tracking-tight">Painel de monitoramento</h1><p className="text-muted-foreground">Visão geral dos cursos acompanhados em {connectionRef ?? 'Moodle'}.</p></div>
        <Button type="button" variant="outline" onClick={refreshAll}><RefreshCw className="mr-2 h-4 w-4" /> Atualizar Tudo</Button>
      </header>

      <section className="space-y-4" aria-labelledby="signals-title">
        <div className="flex items-center justify-between"><h2 id="signals-title" className="text-lg font-semibold">Sinais do monitoramento</h2><span className="text-xs text-muted-foreground">Cada métrica possui atualização própria</span></div>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
          <StatCard title="Cursos acompanhados" value={metricValue(summary.activeCourses, isSummaryLoading, summaryQuery.isError)} subtitle="escopo de Meus Cursos" icon={BookOpen} variant="pending" onRefresh={() => refresh('summary')} refreshing={summaryQuery.isFetching} />
          <StatCard title="Eventos hoje" value={metricValue(summary.todayEvents, isSummaryLoading, summaryQuery.isError)} subtitle="agenda local" icon={CalendarDays} variant="pending" onRefresh={() => refresh('summary')} refreshing={summaryQuery.isFetching} />
          <StatCard title="Tarefas para hoje" value={metricValue(summary.todayTasks, isSummaryLoading, summaryQuery.isError)} subtitle="com vencimento hoje" icon={CheckSquare} variant="risk" onRefresh={() => refresh('summary')} refreshing={summaryQuery.isFetching} />
          <StatCard title="Entregas para corrigir" value={metricValue(correctionCount, isPendingLoading || isPendingRefreshing, pendingQuery.isError)} subtitle={isPendingLoading || isPendingRefreshing ? 'atualizando todos os cursos' : pendingQuery.isError ? 'não foi possível consultar' : `${correctionCount} aguardando correção`} icon={ClipboardCheck} variant="warning" onRefresh={() => refresh('pending')} refreshing={pendingQuery.isFetching || isPendingRefreshing} />
          <StatCard title="Acessaram nos últimos 7 dias" value={metricValue(summary.activeNormalStudents, isAccessLoading, accessQuery.isError)} subtitle="alunos distintos por conexão" icon={UserCheck} variant="success" onRefresh={() => refresh('access')} refreshing={accessQuery.isFetching} />
        </div>
      </section>

      <Card>
        <CardHeader className="flex-row items-center justify-between space-y-0 pb-3"><div><CardTitle className="flex items-center gap-2 text-lg"><AlertTriangle className="h-5 w-5 text-risk-risco" /> Prioridades de hoje</CardTitle><p className="mt-1 text-xs text-muted-foreground">{isPendingRefreshing ? 'Atualizando a visão geral dos cursos acompanhados…' : `${pendingQuery.data?.data.coursesAnalyzed ?? 0} cursos analisados`}</p></div><Button variant="ghost" size="sm" onClick={() => refresh('pending')} disabled={pendingQuery.isFetching || isPendingRefreshing}><RefreshCw className={`mr-1 h-4 w-4 ${pendingQuery.isFetching || isPendingRefreshing ? 'animate-spin' : ''}`} /> Atualizar</Button></CardHeader>
        <CardContent className="space-y-5">
          {todayItems.length > 0 && <div className="space-y-2"><p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Agenda e tarefas</p><div className="grid gap-2 md:grid-cols-2">{todayItems.map((item) => <div key={item.key} className="flex items-center gap-3 rounded-lg border bg-muted/30 p-3">{item.kind === 'event' ? <CalendarDays className="h-4 w-4 shrink-0 text-primary" /> : <CheckSquare className="h-4 w-4 shrink-0 text-status-warning" />}<div className="min-w-0 flex-1"><p className="truncate text-sm font-medium">{item.title}</p><p className="text-xs text-muted-foreground">{item.detail}{formatTime(item.startsAt) ? ` · ${formatTime(item.startsAt)}` : ''}</p></div><Badge variant="outline">Hoje</Badge></div>)}</div></div>}
          {(todayItems.length > 0 && (coursePendingSummaries.length > 0 || priorityItems.length > 0 || isPendingLoading)) && <div className="border-t pt-4" />}
          <ScrollArea className="max-h-[460px] pr-2"><div className="space-y-2">
            {coursePendingSummaries.length > 0 && <><p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Pendências por curso</p>{coursePendingSummaries.map((course) => <div key={course.courseId} className="rounded-lg border bg-muted/50 p-3"><div className="flex items-start justify-between gap-3"><div className="min-w-0"><Link className="block truncate text-sm font-medium hover:underline" to={`/cursos/${encodeURIComponent(connectionRef ?? '')}/${encodeURIComponent(course.courseId)}?tab=activities`}>{course.courseName}</Link><p className="mt-1 text-xs text-muted-foreground">{course.pendingCorrectionActivities} atividade{course.pendingCorrectionActivities === 1 ? '' : 's'} · {course.pendingCorrectionSubmissions} entrega{course.pendingCorrectionSubmissions === 1 ? '' : 's'} aguardando correção</p><p className="text-xs text-muted-foreground">{course.pendingSubmissionActivities} atividade{course.pendingSubmissionActivities === 1 ? '' : 's'} pendente{course.pendingSubmissionActivities === 1 ? '' : 's'} · {course.pendingSubmissions} entrega{course.pendingSubmissions === 1 ? '' : 's'} não enviada{course.pendingSubmissions === 1 ? '' : 's'}</p></div><div className="flex shrink-0 flex-col items-end gap-1">{course.overdueSubmissions > 0 && <Badge variant="outline" className="border-risk-risco/30 text-risk-risco">{course.overdueSubmissions} vencida{course.overdueSubmissions === 1 ? '' : 's'}</Badge>}<Button variant="outline" size="sm" asChild><Link to={`/cursos/${encodeURIComponent(connectionRef ?? '')}/${encodeURIComponent(course.courseId)}?tab=activities`}>Ver curso</Link></Button></div></div>{(course.studentsAwaitingCorrection > 0 || course.studentsWithPendingSubmissions > 0) && <p className="mt-2 text-[11px] text-muted-foreground">{course.studentsAwaitingCorrection > 0 && `${course.studentsAwaitingCorrection} aluno${course.studentsAwaitingCorrection === 1 ? '' : 's'} aguardando correção`}{course.studentsAwaitingCorrection > 0 && course.studentsWithPendingSubmissions > 0 ? ' · ' : ''}{course.studentsWithPendingSubmissions > 0 && `${course.studentsWithPendingSubmissions} aluno${course.studentsWithPendingSubmissions === 1 ? '' : 's'} com envio pendente`}</p>}{course.isTruncated && <p className="mt-2 text-[11px] text-muted-foreground">Leitura limitada para preservar o desempenho.</p>}{course.warning && <p className="mt-2 text-[11px] text-status-warning">Alguns dados deste curso podem estar incompletos.</p>}</div>)}</>}
            {coursePendingSummaries.length === 0 && priorityItems.length > 0 && <><p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Demandas individuais</p>{priorityItems.map((item: DashboardPriority) => <div key={item.key} className="flex items-center justify-between gap-3 rounded-lg border bg-muted/50 p-3"><div className="min-w-0"><p className="truncate text-sm font-medium"><ScopeLink connectionRef={connectionRef} courseId={item.courseId} studentId={item.studentId}>{item.title}</ScopeLink></p><p className="truncate text-xs text-muted-foreground">{item.detail}</p></div><Badge variant="outline" className={item.level === 'risk' ? 'border-risk-risco/30 text-risk-risco' : 'border-status-warning/30 text-status-warning'}>{item.title === 'Atividade para corrigir' ? 'Correção' : item.level === 'risk' ? 'Risco' : 'Atenção'}</Badge></div>)}</>}
            {(isPendingLoading || isPendingRefreshing) && <div className="space-y-2"><Skeleton className="h-16" /><Skeleton className="h-16" /><p className="py-2 text-center text-xs text-muted-foreground">Consultando as atividades dos cursos acompanhados. O resultado será atualizado automaticamente.</p></div>}
            {!isPendingLoading && !isPendingRefreshing && coursePendingSummaries.length === 0 && priorityItems.length === 0 && todayItems.length === 0 && <p className="py-6 text-center text-sm text-muted-foreground">Nenhuma prioridade pendente hoje.</p>}
          </div></ScrollArea>
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="flex-row items-center justify-between space-y-0 pb-3"><CardTitle className="flex items-center gap-2 text-lg"><UsersRound className="h-5 w-5 text-primary" /> Acesso dos alunos</CardTitle><Button variant="ghost" size="sm" onClick={() => refresh('access')} disabled={accessQuery.isFetching}><RefreshCw className={`mr-1 h-4 w-4 ${accessQuery.isFetching ? 'animate-spin' : ''}`} /> Atualizar</Button></CardHeader>
        <CardContent className="space-y-5"><div className="grid gap-4 sm:grid-cols-3"><div><p className="text-3xl font-bold">{metricValue(summary.activeStudents, isAccessLoading, accessQuery.isError)}</p><p className="text-sm text-muted-foreground">alunos matriculados únicos</p></div><div><p className="text-2xl font-bold">{metricValue(summary.studentsAtRisk, isAccessLoading, accessQuery.isError)}</p><p className="text-sm text-muted-foreground">sem acesso há 14+ dias</p></div><div><p className="text-2xl font-bold">{metricValue(neverAccessedCount, isAccessLoading, accessQuery.isError)}</p><p className="text-sm text-muted-foreground">nunca acessaram</p></div></div><div className="space-y-4"><p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Distribuição por frequência de acesso</p>{accessSegments.length > 0 ? accessSegments.map((segment) => { const total = summary.activeStudents ?? 0; const width = total > 0 ? Math.min(100, (segment.students / total) * 100) : 0; const tone = segment.key === 'never' ? 'bg-destructive' : segment.tone === 'success' ? 'bg-status-success' : segment.tone === 'warning' ? 'bg-status-warning' : 'bg-risk-risco'; return <div key={segment.key} className="space-y-1.5"><div className="flex justify-between gap-3 text-xs"><span>{segment.label}</span><span className="font-medium">{segment.students}</span></div><div className="h-2 overflow-hidden rounded-full bg-muted"><div className={`h-full rounded-full ${tone} transition-all`} style={{ width: `${width}%` }} /></div></div>; }) : <div className="h-2 rounded-full bg-muted" />}</div>{accessSnapshots.length > 0 && <div className="space-y-3 border-t pt-4"><div><p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Evolução diária · últimos 15 dias</p><p className="mt-1 text-xs text-muted-foreground">Cada ponto é um snapshot agregado do dia; o histórico começa no primeiro carregamento.</p></div><div className="flex items-end gap-1 overflow-x-auto pb-1" aria-label="Evolução diária de acessos e riscos">{accessSnapshots.map((snapshot) => <div key={snapshot.date} className="flex min-w-8 flex-1 flex-col items-center gap-1" title={`${formatSnapshotDate(snapshot.date)} · ${snapshot.totalStudents} alunos · ${snapshot.studentsAtRisk} em risco`}><div className="flex h-20 w-full items-end justify-center gap-0.5 rounded bg-muted/40 px-0.5 pt-1">{[['bg-status-success', snapshot.recentStudents], ['bg-status-warning', snapshot.lowAccessStudents], ['bg-risk-risco', snapshot.staleStudents], ['bg-destructive', snapshot.neverAccessedStudents]].map(([tone, value]) => <div key={tone as string} className={`w-1.5 rounded-t ${tone as string}`} style={{ height: `${Math.max(value as number, value as number > 0 ? 4 : 0) / snapshotMax * 100}%` }} />)}</div><span className="text-[10px] text-muted-foreground">{formatSnapshotDate(snapshot.date)}</span></div>)}</div><div className="flex flex-wrap gap-x-4 gap-y-1 text-[11px] text-muted-foreground"><span><i className="mr-1 inline-block h-2 w-2 rounded-sm bg-status-success" />0–7 dias</span><span><i className="mr-1 inline-block h-2 w-2 rounded-sm bg-status-warning" />8–14 dias</span><span><i className="mr-1 inline-block h-2 w-2 rounded-sm bg-risk-risco" />14+ dias</span><span><i className="mr-1 inline-block h-2 w-2 rounded-sm bg-destructive" />nunca</span></div></div>}<p className="text-xs text-muted-foreground">Baixo acesso representa 8–14 dias; sem acesso representa 14+ dias; “nunca acessaram” não possuem registro de acesso.</p></CardContent>
      </Card>

      {notices.length > 0 && <details className="rounded-md border border-amber-200/70 bg-amber-50/40 text-sm dark:border-amber-900/70 dark:bg-amber-950/10"><summary className="flex cursor-pointer list-none items-center gap-2 px-3 py-2 text-amber-900 dark:text-amber-100"><AlertCircle className="h-4 w-4 shrink-0" /><span>{notices.length} aviso{notices.length === 1 ? '' : 's'} de atualização</span><span className="ml-auto text-xs text-muted-foreground">Ver detalhes</span></summary><ul className="space-y-1 border-t border-amber-200/70 px-8 py-3 text-xs text-muted-foreground dark:border-amber-900/70">{notices.map((notice) => <li key={notice}>{notice}</li>)}</ul></details>}
    </main>
  );
}
