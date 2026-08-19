import { FormEvent, useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AlertCircle, CheckCircle2, Clock3, Download, FileBarChart2, FolderTree, Loader2, RefreshCw, UsersRound, XCircle } from 'lucide-react';
import { toast } from 'sonner';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { useConnectionScope } from '../connections/useConnectionScope';
import { coursesGateway } from '../courses/courses-gateway';
import { reportsGateway, type ReportJob } from './reports-gateway';

const REPORT_OPTIONS: Array<{ value: 'grades'; title: string; description: string; icon: typeof FileBarChart2 }> = [
  { value: 'grades', title: 'Notas do curso', description: 'Nota total do curso por aluno, com percentual, média e alunos sem nota.', icon: FileBarChart2 },
];

const statusLabels: Record<ReportJob['status'], string> = {
  queued: 'Na fila',
  running: 'Processando',
  completed: 'Disponível',
  failed: 'Falhou',
};

function formatDate(value?: string) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

function reportTitle(type: ReportJob['reportType']) {
  if (type === 'grades') return 'Notas do curso';
  if (type === 'weekly') return 'Desempenho semanal';
  if (type === 'overview') return 'Visão de acompanhamento';
  if (type === 'completion') return 'Conclusão provável';
  return 'Relatório';
}

function scopeLabel(job: ReportJob) {
  return job.scopeType === 'category' ? `Categoria · ${job.categoryPath}` : job.scopeType === 'courses' ? `${job.totalCourses} cursos selecionados` : `Curso · ${job.courseId}`;
}

function StatusIcon({ status }: { status: ReportJob['status'] }) {
  if (status === 'completed') return <CheckCircle2 className="h-4 w-4 text-status-success" />;
  if (status === 'failed') return <XCircle className="h-4 w-4 text-destructive" />;
  if (status === 'running') return <Loader2 className="h-4 w-4 animate-spin text-primary" />;
  return <Clock3 className="h-4 w-4 text-status-warning" />;
}

export function ReportsPage() {
  const queryClient = useQueryClient();
  const { connectionRef } = useConnectionScope();
  const [reportType, setReportType] = useState<'grades'>('grades');
  const [scopeType, setScopeType] = useState<ReportJob['scopeType']>('category');
  const [categoryPath, setCategoryPath] = useState('');
  const [courseId, setCourseId] = useState('');
  const [statusMessage, setStatusMessage] = useState('');
  const initializedNotifications = useRef(false);
  const notifiedStates = useRef(new Set<string>());

  const categories = useQuery({
    queryKey: ['app', 'reports', 'categories', connectionRef],
    queryFn: () => coursesGateway.hierarchy(connectionRef),
    enabled: Boolean(connectionRef),
    staleTime: 60_000,
  });
  const courses = useQuery({
    queryKey: ['app', 'reports', 'courses', connectionRef, categoryPath],
    queryFn: () => coursesGateway.listAllByCategory(categoryPath, connectionRef, 100),
    enabled: Boolean(connectionRef && scopeType === 'course' && categoryPath),
    staleTime: 60_000,
  });
  const jobs = useQuery({
    queryKey: ['app', 'reports', 'jobs'],
    queryFn: () => reportsGateway.jobs(1, 30),
    refetchInterval: (query) => query.state.data?.data.some((job) => job.status === 'queued' || job.status === 'running') ? 10_000 : 30_000,
    staleTime: 2_000,
  });
  const createJob = useMutation({
    mutationFn: reportsGateway.createJob,
    onSuccess: () => {
      setStatusMessage('Relatório adicionado à fila. Você será avisado quando o arquivo estiver disponível.');
      setCourseId('');
      void queryClient.invalidateQueries({ queryKey: ['app', 'reports', 'jobs'] });
    },
    onError: (error) => setStatusMessage(error instanceof Error ? error.message : 'Não foi possível solicitar o relatório.'),
  });

  useEffect(() => {
    const items = jobs.data?.data ?? [];
    if (!initializedNotifications.current) {
      items.forEach((job) => notifiedStates.current.add(`${job.id}:${job.status}`));
      initializedNotifications.current = true;
      return;
    }

    items.forEach((job) => {
      const stateKey = `${job.id}:${job.status}`;
      if (notifiedStates.current.has(stateKey) || (job.status !== 'completed' && job.status !== 'failed')) return;
      notifiedStates.current.add(stateKey);
      if (job.status === 'completed') {
        toast.success('Relatório disponível', { description: `${reportTitle(job.reportType)} · ${scopeLabel(job)}` });
        setStatusMessage(`O relatório “${reportTitle(job.reportType)}” foi concluído e já pode ser baixado.`);
      } else {
        toast.error('Falha ao gerar relatório', { description: job.errorMessage ?? reportTitle(job.reportType) });
        setStatusMessage(`O relatório “${reportTitle(job.reportType)}” não pôde ser concluído.`);
      }
    });
  }, [jobs.data?.data]);

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (!connectionRef) {
      setStatusMessage('Selecione uma conexão Moodle antes de solicitar um relatório.');
      return;
    }
    if (scopeType === 'category' && !categoryPath) {
      setStatusMessage('Selecione a categoria que deverá entrar no relatório.');
      return;
    }
    if (scopeType === 'course' && !courseId) {
      setStatusMessage('Selecione o curso que deverá entrar no relatório.');
      return;
    }
    setStatusMessage('');
    createJob.mutate({
      reportType,
      scopeType,
      connectionRef,
      categoryPath: scopeType === 'category' ? categoryPath : undefined,
      courseId: scopeType === 'course' ? courseId : undefined,
    });
  };

  return (
    <main className="space-y-6 animate-fade-in" aria-labelledby="reports-title">
      <header className="page-heading">
        <div>
          <p className="eyebrow">GESTÃO</p>
          <h1 id="reports-title">Relatórios</h1>
          <p>Escolha o relatório e o escopo. A geração acontece em segundo plano e o arquivo fica disponível nesta tela.</p>
        </div>
        <Button type="button" variant="outline" onClick={() => { void categories.refetch(); void jobs.refetch(); }} disabled={categories.isFetching || jobs.isFetching}>
          <RefreshCw className={categories.isFetching || jobs.isFetching ? 'animate-spin' : ''} />Atualizar
        </Button>
      </header>

      <section className="grid gap-6 lg:grid-cols-[minmax(0,1.15fr)_minmax(320px,0.85fr)]" aria-label="Solicitar relatório">
        <Card>
          <CardHeader>
            <div className="flex items-start gap-3">
              <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary"><FileBarChart2 className="h-5 w-5" /></div>
              <div><CardTitle className="text-lg">Novo relatório de notas</CardTitle><CardDescription>Nota total do curso por aluno, pronta para baixar em Excel formatado.</CardDescription></div>
            </div>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleSubmit} className="space-y-5">
              <div className="grid max-w-xl gap-3">
                {REPORT_OPTIONS.map((option) => {
                  const Icon = option.icon;
                  const selected = reportType === option.value;
                  return <button key={option.value} type="button" aria-pressed={selected} onClick={() => setReportType(option.value)} className={`rounded-lg border p-4 text-left transition-colors ${selected ? 'border-primary bg-primary/5 ring-1 ring-primary' : 'hover:bg-muted/40'}`}><Icon className={`h-5 w-5 ${selected ? 'text-primary' : 'text-muted-foreground'}`} /><p className="mt-3 text-sm font-medium">{option.title}</p><p className="mt-1 text-xs leading-relaxed text-muted-foreground">{option.description}</p></button>;
                })}
              </div>

              <div className="space-y-3">
                <div><p className="text-sm font-medium">Escopo do relatório</p><p className="text-xs text-muted-foreground">Gere para uma categoria inteira ou para um único curso.</p></div>
                <div className="grid gap-3 sm:grid-cols-2">
                  <button type="button" aria-pressed={scopeType === 'category'} onClick={() => { setScopeType('category'); setCourseId(''); }} className={`flex items-center gap-3 rounded-lg border p-3 text-left ${scopeType === 'category' ? 'border-primary bg-primary/5 ring-1 ring-primary' : 'hover:bg-muted/40'}`}><FolderTree className="h-5 w-5 text-primary" /><span><span className="block text-sm font-medium">Categoria inteira</span><span className="block text-xs text-muted-foreground">Todos os cursos visíveis da categoria.</span></span></button>
                  <button type="button" aria-pressed={scopeType === 'course'} onClick={() => setScopeType('course')} className={`flex items-center gap-3 rounded-lg border p-3 text-left ${scopeType === 'course' ? 'border-primary bg-primary/5 ring-1 ring-primary' : 'hover:bg-muted/40'}`}><UsersRound className="h-5 w-5 text-primary" /><span><span className="block text-sm font-medium">Curso específico</span><span className="block text-xs text-muted-foreground">Apenas uma turma selecionada.</span></span></button>
                </div>
              </div>

              <div className="grid gap-4 border-t pt-5 sm:grid-cols-2">
                <label className="grid gap-1.5 text-sm font-medium">Conexão Moodle<div className="flex h-10 items-center rounded-md border bg-muted/30 px-3 text-sm text-muted-foreground">{connectionRef ?? 'Nenhuma conexão selecionada'}</div></label>
                <label className="grid gap-1.5 text-sm font-medium">Categoria Moodle<Select value={categoryPath} onValueChange={(value) => { setCategoryPath(value); setCourseId(''); }} disabled={categories.isPending || !categories.data?.data.length}><SelectTrigger><SelectValue placeholder={categories.isPending ? 'Carregando categorias…' : 'Selecione uma categoria'} /></SelectTrigger><SelectContent>{categories.data?.data.map((category) => <SelectItem key={category.path} value={category.path}>{category.name} · {category.courseCount} cursos</SelectItem>)}</SelectContent></Select></label>
                {scopeType === 'course' && <label className="grid gap-1.5 text-sm font-medium sm:col-span-2">Curso específico<Select value={courseId} onValueChange={setCourseId} disabled={!categoryPath || courses.isPending || !courses.data?.data.length}><SelectTrigger><SelectValue placeholder={courses.isPending ? 'Carregando cursos…' : 'Selecione um curso'} /></SelectTrigger><SelectContent>{courses.data?.data.map((course) => <SelectItem key={course.courseId} value={course.courseId}>{course.fullName} · {course.courseId}</SelectItem>)}</SelectContent></Select>{categoryPath && courses.data?.data.length === 0 && !courses.isPending && <span className="text-xs font-normal text-muted-foreground">Nenhum curso disponível nessa categoria.</span>}</label>}
              </div>

              {statusMessage && <div className="flex items-start gap-2 rounded-md border border-primary/20 bg-primary/5 p-3 text-sm" role="status"><AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-primary" />{statusMessage}</div>}
              <div className="flex items-center justify-between gap-3 border-t pt-4"><p className="text-xs text-muted-foreground">A solicitação não bloqueia a navegação nem a consulta ao Moodle.</p><Button type="submit" disabled={createJob.isPending || !connectionRef}><FileBarChart2 />{createJob.isPending ? 'Enviando…' : 'Solicitar relatório'}</Button></div>
            </form>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle className="text-lg">Como funciona</CardTitle><CardDescription>O processamento é acompanhado automaticamente.</CardDescription></CardHeader>
          <CardContent className="space-y-4 text-sm">
            <div className="flex gap-3"><span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-primary/10 font-semibold text-primary">1</span><div><p className="font-medium">Escolha o relatório de notas</p><p className="mt-1 text-xs leading-relaxed text-muted-foreground">A leitura usa a nota total do curso retornada pelo Moodle para cada aluno.</p></div></div>
            <div className="flex gap-3"><span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-primary/10 font-semibold text-primary">2</span><div><p className="font-medium">Defina o escopo</p><p className="mt-1 text-xs leading-relaxed text-muted-foreground">Uma categoria inteira é útil para comparações; um curso serve para uma análise focada.</p></div></div>
            <div className="flex gap-3"><span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-primary/10 font-semibold text-primary">3</span><div><p className="font-medium">Baixe quando terminar</p><p className="mt-1 text-xs leading-relaxed text-muted-foreground">O sistema avisa e mantém o Excel formatado no histórico.</p></div></div>
          </CardContent>
        </Card>
      </section>

      <section aria-labelledby="report-history-title">
        <Card>
          <CardHeader><div className="flex items-center justify-between gap-3"><div><CardTitle id="report-history-title" className="text-lg">Relatórios solicitados</CardTitle><CardDescription>Histórico dos arquivos gerados para sua conta.</CardDescription></div>{jobs.data?.meta.total !== undefined && <Badge variant="outline">{jobs.data.meta.total} solicitações</Badge>}</div></CardHeader>
          <CardContent>{jobs.isPending ? <div className="flex items-center gap-2 py-8 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando histórico…</div> : jobs.isError ? <div className="flex items-center gap-2 py-8 text-sm text-destructive" role="alert"><AlertCircle className="h-4 w-4" />Não foi possível carregar o histórico de relatórios.</div> : jobs.data?.data.length === 0 ? <div className="flex flex-col items-center gap-2 rounded-lg border border-dashed py-12 text-center"><FileBarChart2 className="h-8 w-8 text-muted-foreground/50" /><p className="text-sm font-medium">Nenhum relatório solicitado</p><p className="text-xs text-muted-foreground">Os arquivos gerados aparecerão aqui.</p></div> : <div className="divide-y">{jobs.data?.data.map((job) => <article key={job.id} className="flex flex-col gap-3 py-4 md:flex-row md:items-center md:justify-between"><div className="min-w-0"><div className="flex flex-wrap items-center gap-2"><StatusIcon status={job.status} /><p className="font-medium">{reportTitle(job.reportType)}</p><Badge variant={job.status === 'completed' ? 'default' : job.status === 'failed' ? 'destructive' : 'outline'}>{statusLabels[job.status]}</Badge></div><p className="mt-1 text-sm text-muted-foreground">{scopeLabel(job)} · {job.connectionRef}</p><p className="mt-1 text-xs text-muted-foreground">Solicitado em {formatDate(job.requestedAt)}{job.status === 'running' ? ` · ${job.processedCourses}/${job.totalCourses || '…'} cursos` : ''}</p>{job.status === 'running' && <div className="mt-2 h-1.5 max-w-md overflow-hidden rounded-full bg-muted"><div className="h-full rounded-full bg-primary transition-all" style={{ width: `${job.progressPercent}%` }} /></div>}{job.status === 'failed' && <p className="mt-1 text-xs text-destructive">{job.errorMessage ?? 'Falha sem detalhes.'}</p>}</div>{job.status === 'completed' && job.downloadUrl && <Button asChild variant="outline" size="sm"><a href={job.downloadUrl} download><Download />Baixar Excel</a></Button>}</article>)}</div>}</CardContent>
        </Card>
      </section>
    </main>
  );
}
