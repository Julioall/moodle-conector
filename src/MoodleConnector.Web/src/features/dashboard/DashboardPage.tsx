import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Activity, AlertCircle, BookOpen, ClipboardCheck, RefreshCw, UsersRound } from 'lucide-react';
import { Link, useSearchParams } from 'react-router-dom';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { StatCard } from '@/components/ui/StatCard';
import { connectionDisplayName, useConnectionScope } from '../connections/useConnectionScope';
import { dashboardGateway } from './dashboard-gateway';

function ScopeLink({ connectionRef, courseId, studentId, children }: { connectionRef?: string; courseId?: string; studentId?: string; children: React.ReactNode }) {
  if (!connectionRef || !courseId || !studentId) return <>{children}</>;
  return <Link className="hover:underline" to={`/alunos/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}/${encodeURIComponent(studentId)}`}>{children}</Link>;
}

export function DashboardPage() {
  const [params] = useSearchParams();
  const { connectionRef, selectedConnection } = useConnectionScope();
  const courseId = params.get('courseId') ?? undefined;
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: ['app', 'dashboard', connectionRef, courseId],
    queryFn: () => dashboardGateway.get(connectionRef, courseId),
  });
  const generatedAt = query.data?.meta.generatedAt;

  return (
    <main className="space-y-6" aria-labelledby="dashboard-title">
      <header className="flex flex-col gap-3 border-b pb-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="mb-1 text-[10px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">Operacional</p>
          <h1 id="dashboard-title" className="text-2xl font-semibold tracking-tight">Resumo da semana</h1>
          <p className="mt-1 text-sm text-muted-foreground">Sinais determinísticos para orientar o acompanhamento manual.</p>
        </div>
        <div className="flex items-center gap-3 text-xs text-muted-foreground">
          <div className="text-right">
            <p className="font-medium text-foreground">{connectionDisplayName(selectedConnection)}</p>
            {generatedAt && <p>Atualizado em {new Date(generatedAt).toLocaleString('pt-BR')}</p>}
          </div>
          <Button
            variant="outline"
            size="icon"
            className="h-9 w-9"
            aria-label="Atualizar resumo"
            title="Atualizar resumo"
            onClick={() => void queryClient.invalidateQueries({ queryKey: ['app', 'dashboard'] })}
          >
            <RefreshCw className={`h-4 w-4 ${query.isFetching ? 'animate-spin' : ''}`} />
          </Button>
        </div>
      </header>

      {query.isPending && (
        <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4" aria-label="Carregando resumo">
          {[1, 2, 3, 4].map((item) => <Skeleton key={item} className="h-32 rounded-lg" />)}
        </section>
      )}

      {query.isError && <Card><CardContent className="p-6"><p role="alert">Não foi possível carregar o resumo.</p></CardContent></Card>}

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
            <h2 id="monitoring-signals-title" className="flex items-center gap-2 text-lg font-semibold">
              <Activity className="h-5 w-5 text-primary" /> Sinais do monitoramento
            </h2>
            <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <StatCard title="Cursos em andamento" value={query.data.data.summary.activeCourses} subtitle="cursos ativos" icon={BookOpen} variant="pending" />
              <StatCard title="Entregas pendentes" value={query.data.data.summary.pendingDeliveries} subtitle="não entregues" icon={ClipboardCheck} variant="warning" />
              <StatCard title="Aguardando correção" value={query.data.data.summary.awaitingGrading} subtitle="somente leitura" icon={ClipboardCheck} variant="risk" />
              <StatCard title="Alunos em atenção" value={query.data.data.summary.studentsNeedingAttention} subtitle={`${query.data.data.summary.studentsAtRisk} em risco`} icon={UsersRound} variant="danger" />
            </div>
          </section>

          <section className="grid gap-6 lg:grid-cols-2">
            <Card>
              <CardHeader className="pb-3"><CardTitle className="flex items-center gap-2 text-lg"><AlertCircle className="h-5 w-5 text-risk-risco" /> Prioridades</CardTitle></CardHeader>
              <CardContent className="space-y-3">
                {query.data.data.priorities.length === 0 ? (
                  <div className="py-6 text-center text-sm text-muted-foreground"><ClipboardCheck className="mx-auto mb-2 h-8 w-8 opacity-50" />Nenhuma prioridade pendente.</div>
                ) : query.data.data.priorities.slice(0, 6).map((item) => (
                  <div key={item.key} className="flex items-start justify-between gap-3 rounded-lg border bg-muted/20 p-3 transition-colors hover:bg-muted/40">
                    <div className="min-w-0"><p className="truncate text-sm font-medium"><ScopeLink connectionRef={connectionRef} courseId={item.courseId} studentId={item.studentId}>{item.title}</ScopeLink></p><p className="mt-1 text-xs text-muted-foreground">{item.detail}</p></div>
                    <Badge variant="outline" className={item.level === 'risk' ? 'border-risk-risco/30 text-risk-risco' : 'border-status-warning/30 text-status-warning'}>{item.level === 'risk' ? 'Risco' : 'Atenção'}</Badge>
                  </div>
                ))}
              </CardContent>
            </Card>

            <Card>
              <CardHeader className="pb-3"><CardTitle className="flex items-center gap-2 text-lg"><ClipboardCheck className="h-5 w-5 text-status-warning" /> Atividades aguardando ação</CardTitle></CardHeader>
              <CardContent className="space-y-3">
                {query.data.data.activitiesToReview.length === 0 ? (
                  <div className="py-6 text-center text-sm text-muted-foreground"><ClipboardCheck className="mx-auto mb-2 h-8 w-8 opacity-50" />Nenhuma atividade aguardando ação.</div>
                ) : query.data.data.activitiesToReview.slice(0, 6).map((item) => (
                  <div key={item.key} className="rounded-lg border border-status-warning/25 p-3"><p className="text-sm font-medium"><ScopeLink connectionRef={connectionRef} courseId={item.courseId} studentId={item.studentId}>{item.title}</ScopeLink></p><p className="mt-1 text-xs text-muted-foreground">{item.detail}</p></div>
                ))}
              </CardContent>
            </Card>
          </section>

          <Card>
            <CardHeader className="pb-3"><CardTitle className="flex items-center gap-2 text-lg"><Activity className="h-5 w-5 text-primary" /> Atividade recente</CardTitle></CardHeader>
            <CardContent>
              {query.data.data.recentActivity.length === 0 ? <p className="py-4 text-sm text-muted-foreground">Nenhuma atividade recente.</p> : <div className="space-y-3">{query.data.data.recentActivity.slice(0, 8).map((item) => <div key={item.key} className="flex items-start gap-3 border-b pb-3 last:border-0 last:pb-0"><div className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-accent text-primary"><Activity className="h-3.5 w-3.5" /></div><div className="min-w-0"><p className="text-sm font-medium"><ScopeLink connectionRef={connectionRef} courseId={item.courseId} studentId={item.studentId}>{item.title}</ScopeLink></p><p className="text-xs text-muted-foreground">{item.detail}{item.occurredAt ? ` · ${new Date(item.occurredAt).toLocaleString('pt-BR')}` : ''}</p></div></div>)}</div>}
            </CardContent>
          </Card>
        </>
      )}
    </main>
  );
}

