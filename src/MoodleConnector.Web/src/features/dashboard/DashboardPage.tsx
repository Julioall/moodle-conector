import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { AlertCircle, AlertTriangle, AreaChart, BarChart3, BookOpen, CalendarDays, CheckSquare, ChartLine, ClipboardCheck, RefreshCw, UserCheck, UsersRound } from 'lucide-react';
import { Link } from 'react-router-dom';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { ScrollArea } from '@/components/ui/scroll-area';
import { Skeleton } from '@/components/ui/skeleton';
import { StatCard } from '@/components/ui/StatCard';
import { useConnectionScope } from '../connections/useConnectionScope';
import { dashboardGateway, type DashboardAccessMetric, type DashboardAccessSnapshot, type DashboardPendingMetric, type DashboardPriority, type DashboardSummaryMetric } from './dashboard-gateway';

const DASHBOARD_STALE_TIME = 5 * 60_000;
const DASHBOARD_SUMMARY_STALE_TIME = 0;
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

const DAILY_CHART_OPTIONS = [
  { key: 'bars', label: 'Barras agrupadas', icon: BarChart3 },
  { key: 'lines', label: 'Linhas por faixa', icon: ChartLine },
  { key: 'area', label: 'Área empilhada', icon: AreaChart },
] as const;

type DailyChartType = (typeof DAILY_CHART_OPTIONS)[number]['key'];
type SnapshotSeriesKey = 'recent' | 'low' | 'stale' | 'never';

const DAILY_CHART_SERIES: ReadonlyArray<{ key: SnapshotSeriesKey; label: string; color: string; getValue: (snapshot: DashboardAccessSnapshot) => number }> = [
  { key: 'recent', label: '0–7 dias', color: 'hsl(var(--status-success))', getValue: (snapshot) => snapshot.recentStudents },
  { key: 'low', label: '8–14 dias', color: 'hsl(var(--status-warning))', getValue: (snapshot) => snapshot.lowAccessStudents },
  { key: 'stale', label: '14+ dias', color: 'hsl(var(--risk-risco))', getValue: (snapshot) => snapshot.staleStudents },
  { key: 'never', label: 'Nunca', color: 'hsl(var(--destructive))', getValue: (snapshot) => snapshot.neverAccessedStudents },
];

function getNiceChartMax(value: number) {
  if (value <= 1) return 1;
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const normalized = value / magnitude;
  const step = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
  return step * magnitude;
}

function formatAxisValue(value: number) {
  return Number.isInteger(value) ? String(value) : value.toLocaleString('pt-BR', { maximumFractionDigits: 1 });
}

function DailyAccessChart({ snapshots }: { snapshots: DashboardAccessSnapshot[] }) {
  const [chartType, setChartType] = useState<DailyChartType>('bars');
  const chartWidth = 760;
  const chartHeight = 236;
  const margin = { top: 14, right: 16, bottom: 34, left: 48 };
  const plotWidth = chartWidth - margin.left - margin.right;
  const plotHeight = chartHeight - margin.top - margin.bottom;
  const maxStudents = Math.max(1, ...snapshots.map((snapshot) => Math.max(snapshot.totalStudents, DAILY_CHART_SERIES.reduce((total, series) => total + series.getValue(snapshot), 0))));
  const scaleMax = getNiceChartMax(maxStudents);
  const ticks = [0, 0.25, 0.5, 0.75, 1].map((ratio) => scaleMax * ratio);
  const x = (index: number) => snapshots.length <= 1 ? margin.left + plotWidth / 2 : margin.left + (index / (snapshots.length - 1)) * plotWidth;
  const y = (value: number) => margin.top + plotHeight - (Math.max(0, value) / scaleMax) * plotHeight;
  const seriesValue = (snapshot: DashboardAccessSnapshot, seriesIndex: number) => DAILY_CHART_SERIES[seriesIndex].getValue(snapshot);
  const seriesPath = (seriesIndex: number, stacked: boolean) => snapshots.map((snapshot, index) => {
    const value = stacked
      ? DAILY_CHART_SERIES.slice(0, seriesIndex + 1).reduce((total, series) => total + series.getValue(snapshot), 0)
      : seriesValue(snapshot, seriesIndex);
    return `${x(index)},${y(value)}`;
  }).join(' ');
  const stackedAreaPath = (seriesIndex: number) => {
    const upper = snapshots.map((snapshot, index) => {
      const value = DAILY_CHART_SERIES.slice(0, seriesIndex + 1).reduce((total, series) => total + series.getValue(snapshot), 0);
      return `${x(index)},${y(value)}`;
    });
    const lower = snapshots.slice().reverse().map((snapshot, reverseIndex) => {
      const index = snapshots.length - reverseIndex - 1;
      const value = DAILY_CHART_SERIES.slice(0, seriesIndex).reduce((total, series) => total + series.getValue(snapshot), 0);
      return `${x(index)},${y(value)}`;
    });
    return `M ${upper.join(' L ')} L ${lower.join(' L ')} Z`;
  };

  return <div className="space-y-3">
    <div className="flex items-start justify-between gap-3">
      <div><p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Últimos 15 dias</p></div>
      <div className="flex shrink-0 items-center gap-0.5" role="group" aria-label="Tipo de gráfico">
        {DAILY_CHART_OPTIONS.map((option) => { const Icon = option.icon; return <Button key={option.key} type="button" variant={chartType === option.key ? 'secondary' : 'ghost'} size="icon" className="h-7 w-7 rounded-md text-muted-foreground" onClick={() => setChartType(option.key)} title={option.label} aria-label={option.label} aria-pressed={chartType === option.key}><Icon className="h-3.5 w-3.5" /></Button>; })}
      </div>
    </div>
    <div className="overflow-x-auto pb-1">
      <div className="min-w-[560px]">
        <svg viewBox={`0 0 ${chartWidth} ${chartHeight}`} className="h-auto w-full" role="img" aria-label={`Evolução diária de acessos, em alunos, no formato ${DAILY_CHART_OPTIONS.find((option) => option.key === chartType)?.label.toLowerCase()}`}>
          {ticks.map((tick) => <g key={tick}><line x1={margin.left} x2={chartWidth - margin.right} y1={y(tick)} y2={y(tick)} stroke="hsl(var(--border))" strokeDasharray="2 3" /><text x={margin.left - 8} y={y(tick) + 3} textAnchor="end" className="fill-muted-foreground" fontSize="10">{formatAxisValue(tick)}</text></g>)}
          {chartType === 'bars' && snapshots.map((snapshot, index) => {
            const barWidth = Math.max(5, Math.min(10, plotWidth / Math.max(snapshots.length * 4, 1)));
            const barGap = 2;
            const groupWidth = DAILY_CHART_SERIES.length * barWidth + (DAILY_CHART_SERIES.length - 1) * barGap;
            return <g key={snapshot.date}>{DAILY_CHART_SERIES.map((series, seriesIndex) => { const value = seriesValue(snapshot, seriesIndex); const barX = x(index) - groupWidth / 2 + seriesIndex * (barWidth + barGap); return value > 0 ? <rect key={series.key} x={barX} y={y(value)} width={barWidth} height={Math.max(1, y(0) - y(value))} rx="2" fill={series.color}><title>{`${formatSnapshotDate(snapshot.date)} · ${series.label} · ${value} alunos`}</title></rect> : null; })}</g>;
          })}
          {chartType === 'area' && DAILY_CHART_SERIES.map((series, seriesIndex) => <path key={series.key} d={stackedAreaPath(seriesIndex)} fill={series.color} fillOpacity="0.2" stroke={series.color} strokeWidth="1.5"><title>{series.label}</title></path>)}
          {chartType === 'lines' && DAILY_CHART_SERIES.map((series, seriesIndex) => <g key={series.key}><polyline fill="none" stroke={series.color} strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" points={seriesPath(seriesIndex, false)} />{snapshots.map((snapshot, index) => <circle key={snapshot.date} cx={x(index)} cy={y(seriesValue(snapshot, seriesIndex))} r="2.5" fill={series.color}><title>{`${formatSnapshotDate(snapshot.date)} · ${series.label} · ${seriesValue(snapshot, seriesIndex)} alunos`}</title></circle>)}</g>)}
          {snapshots.map((snapshot, index) => <text key={snapshot.date} x={x(index)} y={chartHeight - 10} textAnchor="middle" className="fill-muted-foreground" fontSize="10">{formatSnapshotDate(snapshot.date)}</text>)}
        </svg>
      </div>
    </div>
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-[11px] text-muted-foreground"><span className="font-medium">Alunos</span>{DAILY_CHART_SERIES.map((series) => <span key={series.key}><i className="mr-1 inline-block h-2 w-2 rounded-sm" style={{ backgroundColor: series.color }} />{series.label}</span>)}</div>
  </div>;
}

export function DashboardPage() {
  const { connectionRef } = useConnectionScope();
  const queryClient = useQueryClient();

  const summaryQuery = useQuery<DashboardSummaryMetric>({
    queryKey: ['app', 'dashboard', 'summary', connectionRef],
    queryFn: () => dashboardGateway.getMetric<DashboardSummaryMetric>('summary', connectionRef),
    enabled: true,
    // Local planner counters must be current when returning from Tasks or Agenda.
    staleTime: DASHBOARD_SUMMARY_STALE_TIME,
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
    if (!connectionRef && metric !== 'summary') return;
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

  return (
    <main className="space-y-6 animate-fade-in" aria-labelledby="dashboard-title">
      <header className="flex flex-col gap-2 md:flex-row md:items-end md:justify-between">
        <div><p className="eyebrow">MONITORAMENTO</p><h1 id="dashboard-title" className="text-2xl font-bold tracking-tight">Painel de monitoramento</h1><p className="text-muted-foreground">Visão geral dos cursos acompanhados em {connectionRef ?? 'Moodle'}.</p></div>
        <Button type="button" variant="outline" onClick={refreshAll}><RefreshCw className="mr-2 h-4 w-4" /> Atualizar Tudo</Button>
      </header>

      <section className="space-y-4" aria-labelledby="signals-title">
        <div className="flex items-center justify-between"><h2 id="signals-title" className="text-lg font-semibold">Sinais do monitoramento</h2><span className="text-xs text-muted-foreground">Cada métrica possui atualização própria</span></div>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
          <StatCard title="Meus Cursos" value={metricValue(summary.activeCourses, isSummaryLoading, summaryQuery.isError)} subtitle="cursos acompanhados" icon={BookOpen} variant="pending" onRefresh={() => refresh('summary')} refreshing={summaryQuery.isFetching} />
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
        <CardHeader className="flex-row items-center justify-between space-y-0 pb-3"><CardTitle className="flex items-center gap-2 text-lg"><UsersRound className="h-5 w-5 text-primary" /> Acessos hoje</CardTitle><Button variant="ghost" size="sm" onClick={() => refresh('access')} disabled={accessQuery.isFetching}><RefreshCw className={`mr-1 h-4 w-4 ${accessQuery.isFetching ? 'animate-spin' : ''}`} /> Atualizar</Button></CardHeader>
        <CardContent className="space-y-5"><div><p className="text-3xl font-bold">{metricValue(summary.activeStudents, isAccessLoading, accessQuery.isError)}</p><p className="text-sm text-muted-foreground">alunos matriculados únicos</p></div><div className="space-y-4"><p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Distribuição por frequência de acesso</p>{accessSegments.length > 0 ? accessSegments.map((segment) => { const total = summary.activeStudents ?? 0; const width = total > 0 ? Math.min(100, (segment.students / total) * 100) : 0; const tone = segment.key === 'never' ? 'bg-destructive' : segment.tone === 'success' ? 'bg-status-success' : segment.tone === 'warning' ? 'bg-status-warning' : 'bg-risk-risco'; return <div key={segment.key} className="space-y-1.5"><div className="flex justify-between gap-3 text-xs"><span>{segment.label}</span><span className="font-medium">{segment.students}</span></div><div className="h-2 overflow-hidden rounded-full bg-muted"><div className={`h-full rounded-full ${tone} transition-all`} style={{ width: `${width}%` }} /></div></div>; }) : <div className="h-2 rounded-full bg-muted" />}</div>{accessSnapshots.length > 0 && <DailyAccessChart snapshots={accessSnapshots} />}</CardContent>
      </Card>

      {notices.length > 0 && <details className="rounded-md border border-amber-200/70 bg-amber-50/40 text-sm dark:border-amber-900/70 dark:bg-amber-950/10"><summary className="flex cursor-pointer list-none items-center gap-2 px-3 py-2 text-amber-900 dark:text-amber-100"><AlertCircle className="h-4 w-4 shrink-0" /><span>{notices.length} aviso{notices.length === 1 ? '' : 's'} de atualização</span><span className="ml-auto text-xs text-muted-foreground">Ver detalhes</span></summary><ul className="space-y-1 border-t border-amber-200/70 px-8 py-3 text-xs text-muted-foreground dark:border-amber-900/70">{notices.map((notice) => <li key={notice}>{notice}</li>)}</ul></details>}
    </main>
  );
}
