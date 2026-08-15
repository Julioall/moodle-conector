import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo } from 'react';
import {
  Activity,
  AlertCircle,
  AlertTriangle,
  BookOpen,
  Calendar,
  CalendarDays,
  CheckSquare,
  ClipboardCheck,
  ExternalLink,
  Filter,
  RefreshCw,
  UserCheck,
} from 'lucide-react';
import { Link, useSearchParams } from 'react-router-dom';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { ScrollArea } from '@/components/ui/scroll-area';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import { StatCard } from '@/components/ui/StatCard';
import { useConnectionScope } from '../connections/useConnectionScope';
import { coursesGateway } from '../courses/courses-gateway';
import { dashboardGateway } from './dashboard-gateway';

function ScopeLink({
  connectionRef,
  courseId,
  studentId,
  children,
}: {
  connectionRef?: string;
  courseId?: string;
  studentId?: string;
  children: React.ReactNode;
}) {
  if (!connectionRef || !courseId || !studentId) return <>{children}</>;

  return (
    <Link
      className="hover:underline"
      to={`/cursos/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}/alunos/${encodeURIComponent(studentId)}`}
    >
      {children}
    </Link>
  );
}

function formatDate(value?: string) {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toLocaleString('pt-BR');
}

function formatRelativeDate(value?: string) {
  if (!value) return null;
  const timestamp = new Date(value).getTime();
  if (Number.isNaN(timestamp)) return null;
  const differenceInMinutes = Math.round((timestamp - Date.now()) / 60_000);
  if (Math.abs(differenceInMinutes) < 1) return 'agora';
  if (Math.abs(differenceInMinutes) < 60) return differenceInMinutes < 0 ? `há ${Math.abs(differenceInMinutes)} min` : `em ${differenceInMinutes} min`;
  const differenceInHours = Math.round(differenceInMinutes / 60);
  if (Math.abs(differenceInHours) < 24) return differenceInHours < 0 ? `há ${Math.abs(differenceInHours)} h` : `em ${differenceInHours} h`;
  const differenceInDays = Math.round(differenceInHours / 24);
  return differenceInDays < 0 ? `há ${Math.abs(differenceInDays)} dia${Math.abs(differenceInDays) === 1 ? '' : 's'}` : `em ${differenceInDays} dia${differenceInDays === 1 ? '' : 's'}`;
}

function formatMetric(value?: number | null) {
  return value == null ? '—' : value;
}

export function DashboardPage() {
  const [params, setParams] = useSearchParams();
  const { connectionRef } = useConnectionScope();
  const courseId = params.get('courseId') ?? undefined;
  const week = params.get('week') === 'last' ? 'last' : 'current';
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: ['app', 'dashboard', connectionRef, courseId, week],
    queryFn: () => dashboardGateway.get(connectionRef, courseId, false, week),
    staleTime: 30_000,
  });
  const coursesQuery = useQuery({
    queryKey: ['app', 'courses', connectionRef],
    queryFn: () => coursesGateway.list(connectionRef, 1, 100),
    staleTime: 60_000,
  });
  const visibleCourses = useMemo(() => (coursesQuery.data?.data ?? []).filter((course) => course.visible !== false), [coursesQuery.data?.data]);
  const courseOptions = useMemo(() => Array.from(new Map(visibleCourses.map((course) => [course.courseId, course.displayName || course.shortName || course.fullName])).entries()), [visibleCourses]);

  return (
    <main className="space-y-6 animate-fade-in" aria-labelledby="dashboard-title">
      <header className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
        <div>
          <h1 id="dashboard-title" className="text-2xl font-bold tracking-tight">Painel de monitoramento</h1>
          <p className="text-muted-foreground">Acompanhe risco, entregas e fila operacional dos cursos monitorados</p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            variant="outline"
            onClick={() => {
              void queryClient.invalidateQueries({ queryKey: ['app', 'dashboard'] });
              void queryClient.invalidateQueries({ queryKey: ['app', 'courses'] });
            }}
            disabled={query.isFetching}
          >
            <RefreshCw className={`mr-2 h-4 w-4 ${query.isFetching ? 'animate-spin' : ''}`} />
            Atualizar
          </Button>
          <Select
            value={week}
            onValueChange={(value) => {
              const next = new URLSearchParams(params);
              if (value === 'last') next.set('week', value);
              else next.delete('week');
              setParams(next, { replace: true });
            }}
          >
            <SelectTrigger className="w-[160px]">
              <Calendar className="mr-2 h-4 w-4" />
              <SelectValue placeholder="Semana atual" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="current">Semana atual</SelectItem>
              <SelectItem value="last">Última semana</SelectItem>
            </SelectContent>
          </Select>
          <Select
            value={courseId ?? 'all'}
            onValueChange={(value) => {
              const next = new URLSearchParams(params);
              if (value === 'all') next.delete('courseId');
              else next.set('courseId', value);
              setParams(next, { replace: true });
            }}
          >
            <SelectTrigger className="w-[190px]">
              <Filter className="mr-2 h-4 w-4" />
              <SelectValue placeholder={coursesQuery.isPending ? 'Carregando cursos' : 'Todos os cursos'} />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Todos os cursos</SelectItem>
              {courseOptions.map(([value, label]) => <SelectItem key={value} value={value}>{label}</SelectItem>)}
            </SelectContent>
          </Select>
        </div>
      </header>

      {query.isPending && (
        <section className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5" aria-label="Carregando resumo">
          {[1, 2, 3, 4, 5].map((item) => <Skeleton key={item} className="h-32 rounded-lg" />)}
        </section>
      )}

      {query.isError && (
        <Card>
          <CardContent className="p-6"><p role="alert">Não foi possível carregar o resumo.</p></CardContent>
        </Card>
      )}

      {query.isSuccess && (
        <>
          {query.data.data.warnings.map((warning) => (
            <Card key={warning} className="border-amber-200 bg-amber-50/60 dark:border-amber-900 dark:bg-amber-950/20">
              <CardContent className="flex items-start gap-3 p-4 text-sm text-amber-900 dark:text-amber-100">
                <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
                <p>{warning}</p>
              </CardContent>
            </Card>
          ))}

          <section className="space-y-4" aria-labelledby="monitoring-signals-title">
            <h2 id="monitoring-signals-title" className="text-lg font-semibold">Sinais do monitoramento</h2>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
              <StatCard title="Eventos hoje" value={formatMetric(query.data.data.summary.todayEvents)} subtitle={query.data.data.summary.todayEvents == null ? 'não informado pelo conector' : query.data.data.summary.todayEvents > 0 ? 'na agenda' : 'nenhum evento hoje'} icon={CalendarDays} variant="pending" />
              <StatCard title="Tarefas para hoje" value={formatMetric(query.data.data.summary.todayTasks)} subtitle={query.data.data.summary.todayTasks == null ? 'não informado pelo conector' : query.data.data.summary.todayTasks > 0 ? 'com vencimento hoje' : 'nenhuma tarefa hoje'} icon={CheckSquare} variant="risk" />
              <StatCard title="Atividades para corrigir" value={formatMetric(query.data.data.summary.activitiesToReview)} subtitle={query.data.data.summary.activitiesToReview == null ? 'selecione um curso' : query.data.data.summary.activitiesToReview > 0 ? `Envio pendente: ${query.data.data.summary.pendingSubmissionAssignments ?? 0} · Correção pendente: ${query.data.data.summary.pendingCorrectionAssignments ?? 0}` : 'fila zerada'} icon={ClipboardCheck} variant="warning" />
              <StatCard title="Alunos Regulares" value={formatMetric(query.data.data.summary.activeNormalStudents)} subtitle={query.data.data.summary.activeNormalStudents == null ? 'selecione um curso' : query.data.data.summary.activeNormalStudents > 0 ? 'monitoramento estável' : 'nenhum aluno regular no momento'} icon={UserCheck} variant="success" />
              <StatCard title="Alunos em risco" value={courseId ? query.data.data.summary.studentsAtRisk : '—'} subtitle={courseId ? (query.data.data.summary.newAtRiskThisWeek != null && query.data.data.summary.newAtRiskThisWeek > 0 ? `+${query.data.data.summary.newAtRiskThisWeek} novos` : 'sinais de risco') : 'selecione um curso'} icon={AlertTriangle} variant="danger" />
            </div>
          </section>

          <section className="grid gap-6 lg:grid-cols-2">
            <Card>
              <CardHeader className="pb-3">
                <CardTitle className="flex items-center gap-2 text-lg"><AlertCircle className="h-5 w-5 text-risk-risco" /> Prioridades - O que fazer agora</CardTitle>
              </CardHeader>
              <CardContent className="p-0">
                <ScrollArea className="h-[300px] px-6 pb-6">
                    <div className="space-y-2">
                      {query.data.data.priorities.slice(0, 3).map((item) => (
                      <div key={item.key} className="flex items-center justify-between gap-3 rounded-lg border bg-muted/50 p-3 transition-colors hover:bg-muted/70">
                        <div className="flex min-w-0 flex-1 items-center gap-3">
                          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-primary/10 text-sm font-medium text-primary">{item.detail.trim().charAt(0).toUpperCase() || '!'}</div>
                          <div className="min-w-0 flex-1">
                            <p className="truncate text-sm font-medium"><ScopeLink connectionRef={connectionRef} courseId={item.courseId} studentId={item.studentId}>{item.title}</ScopeLink></p>
                            <p className="mt-0.5 truncate text-xs text-muted-foreground">{item.detail}</p>
                          </div>
                        </div>
                        <Badge variant="outline" className={item.level === 'risk' ? 'border-risk-risco/30 text-risk-risco' : 'border-status-warning/30 text-status-warning'}>{item.level === 'risk' ? 'Risco' : 'Atenção'}</Badge>
                      </div>
                    ))}
                    {query.data.data.priorities.length === 0 && <p className="py-6 text-center text-sm text-muted-foreground">Nenhuma prioridade pendente.</p>}
                  </div>
                </ScrollArea>
              </CardContent>
            </Card>

            <Card>
              <CardHeader className="pb-3">
                <CardTitle className="flex items-center gap-2 text-lg"><ClipboardCheck className="h-5 w-5 text-status-warning" /> Atividades para corrigir</CardTitle>
              </CardHeader>
              <CardContent className="p-0">
                <ScrollArea className="h-[300px] px-6 pb-6">
                    <div className="space-y-3">
                      {query.data.data.activitiesToReview.slice(0, 6).map((item) => (
                      <div key={item.key} className="rounded-lg border border-status-warning/25 p-3">
                        <div className="flex items-start justify-between gap-3">
                          <div className="min-w-0 flex-1">
                            <p className="truncate text-sm font-medium"><ScopeLink connectionRef={connectionRef} courseId={item.courseId} studentId={item.studentId}>{item.title}</ScopeLink></p>
                            <p className="mt-1 truncate text-xs text-muted-foreground">{item.detail}</p>
                          </div>
                          <Badge variant="outline" className="shrink-0 border-status-warning/30 text-status-warning">Pendente</Badge>
                        </div>
                      </div>
                    ))}
                    {query.data.data.activitiesToReview.length === 0 && <p className="py-6 text-center text-sm text-muted-foreground">Nenhuma atividade aguardando ação.</p>}
                  </div>
                </ScrollArea>
              </CardContent>
            </Card>
          </section>

          <section className="grid gap-6 lg:grid-cols-2">
            <Card>
              <CardHeader className="pb-3"><CardTitle className="flex items-center gap-2 text-lg"><BookOpen className="h-5 w-5 text-primary" /> Visão por Curso</CardTitle></CardHeader>
              <CardContent className="p-0">
                <ScrollArea className="h-[300px] px-6 pb-6">
                  <div className="space-y-3">
                    {visibleCourses.slice(0, 6).map((course) => (
                      <div key={`${course.connectionRef}:${course.courseId}`} className="card-interactive flex items-center justify-between gap-4 rounded-lg border bg-muted/30 p-3">
                        <div className="min-w-0 flex-1">
                          <p className="truncate text-sm font-medium">{course.displayName ?? course.fullName}</p>
                          <div className="mt-1 flex items-center gap-4 text-xs text-muted-foreground">
                            <span>{course.progress != null ? `${Math.round(course.progress)}% de progresso` : 'Progresso não informado'}</span>
                            <span>{course.lastAccessAt ? `Acesso ${formatDate(course.lastAccessAt)}` : 'Sem acesso recente'}</span>
                          </div>
                        </div>
                        <Button size="sm" variant="ghost" asChild>
                          <Link to={`/cursos/${encodeURIComponent(course.connectionRef)}/${encodeURIComponent(course.courseId)}`} aria-label={`Abrir ${course.displayName ?? course.fullName}`}>
                            <ExternalLink className="h-4 w-4" />
                          </Link>
                        </Button>
                      </div>
                    ))}
                    {visibleCourses.length === 0 && <div className="py-6 text-center text-muted-foreground"><BookOpen className="mx-auto mb-2 h-8 w-8 opacity-50" /><p className="text-sm">Nenhum curso encontrado</p><p className="mt-1 text-xs">Selecione uma conexão com cursos disponíveis.</p></div>}
                  </div>
                </ScrollArea>
              </CardContent>
            </Card>
            <Card>
              <CardHeader className="pb-3"><CardTitle className="flex items-center gap-2 text-lg"><Activity className="h-5 w-5 text-primary" /> Atividade recente</CardTitle></CardHeader>
              <CardContent>
                {query.data.data.recentActivity.length === 0 ? <div className="py-6 text-center text-muted-foreground"><Activity className="mx-auto mb-2 h-8 w-8 opacity-50" /><p className="text-sm">Nenhuma atividade recente</p></div> : <div className="space-y-1">{query.data.data.recentActivity.slice(0, 8).map((item, index) => <div key={item.key} className="relative"><div className="flex gap-3 py-2">{index < Math.min(query.data.data.recentActivity.length, 8) - 1 && <div className="absolute bottom-0 left-4 top-10 w-0.5 bg-border" />}<div className="relative flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-accent text-primary"><Activity className="h-4 w-4" /></div><div className="min-w-0 flex-1"><p className="text-sm font-medium"><ScopeLink connectionRef={connectionRef} courseId={item.courseId} studentId={item.studentId}>{item.title}</ScopeLink></p><p className="truncate text-xs text-muted-foreground">{item.detail}</p><p className="mt-0.5 text-xs text-muted-foreground/70">{formatRelativeDate(item.occurredAt) ?? (item.occurredAt ? formatDate(item.occurredAt) : 'Agora')}</p></div></div></div>)}</div>}
              </CardContent>
            </Card>
          </section>
        </>
      )}
    </main>
  );
}
