import { AlertCircle, CheckCircle2, Clock3, Download, FileBarChart2, Loader2, RefreshCw, Trash2, XCircle } from 'lucide-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Progress } from '@/components/ui/progress';
import { reportsGateway, type ReportJob } from './reports-gateway';

const statusLabels: Record<ReportJob['status'], string> = {
  queued: 'Na fila',
  running: 'Em produção',
  completed: 'Disponível',
  failed: 'Falhou',
};

function formatDate(value?: string) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

function reportTitle(type: ReportJob['reportType']) {
  if (type === 'grades') return 'Relatório de notas';
  if (type === 'weekly') return 'Desempenho semanal';
  if (type === 'overview') return 'Visão de acompanhamento';
  if (type === 'completion') return 'Conclusão provável';
  return 'Relatório';
}

function scopeLabel(job: ReportJob) {
  if (job.scopeType === 'category') return `Categoria · ${job.categoryPath ?? '—'}`;
  if (job.scopeType === 'courses') return `${job.totalCourses} cursos selecionados`;
  return '1 curso selecionado';
}

function StatusIcon({ status }: { status: ReportJob['status'] }) {
  if (status === 'completed') return <CheckCircle2 className="h-4 w-4 text-status-success" />;
  if (status === 'failed') return <XCircle className="h-4 w-4 text-destructive" />;
  if (status === 'running') return <Loader2 className="h-4 w-4 animate-spin text-primary" />;
  return <Clock3 className="h-4 w-4 text-status-warning" />;
}

function downloadLabel(job: ReportJob) {
  return job.contentType === 'application/zip' ? 'Baixar ZIP' : 'Baixar Excel';
}

function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  const kilobytes = bytes / 1024;
  if (kilobytes < 1024) return `${kilobytes.toFixed(1)} KB`;
  const megabytes = kilobytes / 1024;
  if (megabytes < 1024) return `${megabytes.toFixed(1)} MB`;
  return `${(megabytes / 1024).toFixed(2)} GB`;
}

export function ReportHistoryPage() {
  const queryClient = useQueryClient();
  const jobs = useQuery({
    queryKey: ['app', 'report-history'],
    queryFn: () => reportsGateway.jobs(1, 50),
    refetchInterval: (query) => query.state.data?.data.some((job) => job.status === 'queued' || job.status === 'running') ? 10_000 : 30_000,
    staleTime: 2_000,
  });
  const deleteJob = useMutation({
    mutationFn: (id: string) => reportsGateway.deleteJob(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['app', 'report-history'] });
      await queryClient.invalidateQueries({ queryKey: ['app', 'report-jobs-notifications'] });
    },
  });

  const reportJobs = jobs.data?.data ?? [];
  const inProduction = reportJobs.filter((job) => job.status === 'queued' || job.status === 'running').length;
  const storageUsedBytes = jobs.data?.meta.storageUsedBytes ?? 0;
  const storageLimitBytes = jobs.data?.meta.storageLimitBytes ?? 300 * 1024 * 1024;
  const storageAvailableBytes = jobs.data?.meta.storageAvailableBytes ?? storageLimitBytes;
  const storagePercent = Math.min(100, storageLimitBytes > 0 ? (storageUsedBytes / storageLimitBytes) * 100 : 0);

  return (
    <main className="space-y-6 animate-fade-in" aria-labelledby="report-history-title">
      <header className="page-heading">
        <div>
          <p className="eyebrow">GESTÃO</p>
          <h1 id="report-history-title">Relatórios</h1>
          <p>Acompanhe os relatórios solicitados em Escolas e baixe os arquivos concluídos.</p>
        </div>
        <Button type="button" variant="outline" onClick={() => void jobs.refetch()} disabled={jobs.isFetching}>
          <RefreshCw className={jobs.isFetching ? 'animate-spin' : ''} />Atualizar
        </Button>
      </header>

      <section className="grid gap-4 sm:grid-cols-2" aria-label="Resumo dos relatórios">
        <Card>
          <CardContent className="flex items-center gap-3 p-5">
            <div className="rounded-lg bg-primary/10 p-2 text-primary"><Loader2 className="h-5 w-5" /></div>
            <div><p className="text-2xl font-semibold">{inProduction}</p><p className="text-sm text-muted-foreground">Relatórios em produção</p></div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="flex items-center gap-3 p-5">
            <div className="rounded-lg bg-emerald-500/10 p-2 text-emerald-600"><FileBarChart2 className="h-5 w-5" /></div>
            <div><p className="text-2xl font-semibold">{reportJobs.filter((job) => job.status === 'completed').length}</p><p className="text-sm text-muted-foreground">Arquivos disponíveis</p></div>
          </CardContent>
        </Card>
      </section>

      <Card>
        <CardHeader>
          <div className="flex items-center justify-between gap-3">
            <div>
              <CardTitle className="text-lg">Armazenamento</CardTitle>
              <CardDescription>Os arquivos gerados compartilham um limite de 300 MB por usuário.</CardDescription>
            </div>
            <span className="text-sm font-medium">{formatBytes(storageUsedBytes)} / {formatBytes(storageLimitBytes)}</span>
          </div>
        </CardHeader>
        <CardContent>
          <Progress value={storagePercent} aria-label={`Uso de armazenamento: ${formatBytes(storageUsedBytes)} de ${formatBytes(storageLimitBytes)}`} indicatorClassName={storagePercent >= 90 ? 'bg-destructive' : storagePercent >= 70 ? 'bg-status-warning' : 'bg-primary'} />
          <div className="mt-2 flex flex-wrap justify-between gap-2 text-xs text-muted-foreground"><span>{formatBytes(storageAvailableBytes)} disponíveis</span><span>{storagePercent.toFixed(1)}% utilizado</span></div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <div className="flex items-center justify-between gap-3">
            <div>
              <CardTitle className="text-lg">Relatórios criados</CardTitle>
              <CardDescription>As solicitações permanecem aqui enquanto estão na fila, em produção ou prontas para download.</CardDescription>
            </div>
            {jobs.data?.meta.total !== undefined && <Badge variant="outline">{jobs.data.meta.total} solicitações</Badge>}
          </div>
        </CardHeader>
        <CardContent>
          {deleteJob.isError && <div className="mb-4 flex items-center gap-2 rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive" role="alert"><AlertCircle className="h-4 w-4" />Não foi possível excluir o relatório. Tente novamente.</div>}
          <span className="sr-only" role="status" aria-live="polite">{jobs.isFetching ? 'Atualizando relatórios.' : ''}</span>
          {jobs.isPending && !jobs.data ? (
            <div className="flex items-center gap-2 py-10 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando relatórios…</div>
          ) : jobs.isError && !jobs.data ? (
            <div className="flex items-center gap-2 py-10 text-sm text-muted-foreground" role="status"><AlertCircle className="h-4 w-4" />Histórico temporariamente indisponível. Use “Atualizar” para tentar novamente.</div>
          ) : reportJobs.length === 0 ? (
            <div className="flex flex-col items-center gap-2 rounded-lg border border-dashed py-12 text-center"><FileBarChart2 className="h-8 w-8 text-muted-foreground/50" /><p className="text-sm font-medium">Nenhum relatório solicitado</p><p className="text-xs text-muted-foreground">Selecione cursos em Escolas para gerar o relatório de notas.</p></div>
          ) : (
            <div className="divide-y">
              {reportJobs.map((job) => (
                <article key={job.id} className="py-4">
                  <div className="flex flex-col gap-3">
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                      <div className="min-w-0">
                        <div className="flex flex-wrap items-center gap-2"><StatusIcon status={job.status} /><p className="font-medium">{reportTitle(job.reportType)}</p><Badge variant={job.status === 'completed' ? 'default' : job.status === 'failed' ? 'destructive' : 'outline'}>{statusLabels[job.status]}</Badge></div>
                        <p className="mt-1 text-sm text-muted-foreground">{scopeLabel(job)} · {job.connectionRef}</p>
                        <p className="mt-1 text-xs text-muted-foreground">Solicitado em {formatDate(job.requestedAt)}{job.status === 'running' ? ` · ${job.processedCourses}/${job.totalCourses || '…'} cursos` : ''}</p>
                      </div>
                      <div className="flex shrink-0 flex-wrap items-center justify-end gap-2" aria-label="Ações do relatório">
                    {job.status === 'completed' && job.downloadUrl && <Button asChild variant="outline" size="sm"><a href={job.downloadUrl} download><Download />{downloadLabel(job)}</a></Button>}
                    {job.status === 'completed' && <Button type="button" variant="ghost" size="sm" className="text-destructive hover:text-destructive" disabled={deleteJob.isPending} onClick={() => { if (window.confirm('Excluir este arquivo gerado? Esta ação libera espaço e remove o relatório do histórico.')) deleteJob.mutate(job.id); }}><Trash2 />Excluir</Button>}
                      </div>
                    </div>
                    {job.courses && job.courses.length > 0 && (
                      <details className="rounded-lg border bg-muted/20 p-3" open={job.courses.length <= 8}>
                        <summary className="cursor-pointer text-xs font-medium">Ver cursos e turmas incluídos ({job.courses.length})</summary>
                        <ul className="mt-3 grid max-h-56 gap-2 overflow-y-auto sm:grid-cols-2">
                          {job.courses.map((course, index) => (
                            <li key={`${course.name}-${course.categoryName ?? ''}-${index}`} className="rounded-md bg-background/70 p-2">
                              <p className="text-sm font-medium">{course.name}</p>
                              <p className="mt-0.5 text-xs text-muted-foreground">{course.categoryName ?? 'Turma não informada'}</p>
                            </li>
                          ))}
                        </ul>
                      </details>
                    )}
                    {job.status === 'running' && <div className="h-1.5 max-w-md overflow-hidden rounded-full bg-muted"><div className="h-full rounded-full bg-primary transition-all" style={{ width: `${job.progressPercent}%` }} /></div>}
                    {job.status === 'failed' && <p className="text-xs text-destructive">{job.errorMessage ?? 'Falha sem detalhes.'}</p>}
                  </div>
                </article>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </main>
  );
}
