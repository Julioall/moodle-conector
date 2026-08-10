import { useQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import { AlertTriangle, ArrowRight, BookOpen, Clock3 } from 'lucide-react';
import { Badge } from '../../components/ui/badge';
import { Button } from '../../components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card';
import { Pagination } from '../../components/ui/pagination';
import { Skeleton } from '../../components/ui/skeleton';
import { useConnectionScope, connectionDisplayName } from '../connections/useConnectionScope';
import { coursesGateway } from '../courses/courses-gateway';
import { pendingGateway, type PendingFilters, type PendingLevel, type PendingType } from './pending-gateway';

const labels: Record<PendingType, string> = {
  no_recent_access: 'Sem acesso recente',
  pending_submission: 'Atividade não entregue',
  awaiting_grading: 'Aguardando correção',
  low_grade: 'Nota baixa',
  upcoming_deadline: 'Prazo próximo',
};
const levelLabels: Record<PendingLevel, string> = { normal: 'Normal', attention: 'Atenção', risk: 'Risco', critical: 'Crítico' };
const levelClasses: Record<PendingLevel, string> = {
  normal: 'border-[hsl(var(--risk-normal)/0.3)] bg-[hsl(var(--risk-normal-bg))] text-[hsl(var(--risk-normal))]',
  attention: 'border-[hsl(var(--risk-atencao)/0.3)] bg-[hsl(var(--risk-atencao-bg))] text-[hsl(var(--risk-atencao))]',
  risk: 'border-[hsl(var(--risk-risco)/0.3)] bg-[hsl(var(--risk-risco-bg))] text-[hsl(var(--risk-risco))]',
  critical: 'border-[hsl(var(--risk-critico)/0.3)] bg-[hsl(var(--risk-critico-bg))] text-[hsl(var(--risk-critico))]',
};

export function PendingPage() {
  const [params, setParams] = useSearchParams();
  const { connectionRef, selectedConnection } = useConnectionScope();
  const courseId = params.get('courseId') || undefined;
  const page = Math.max(Number(params.get('page') || 1), 1);
  const type = (params.get('type') as PendingType) || undefined;
  const level = (params.get('level') as PendingLevel) || undefined;
  const periodDays = params.get('periodDays') ? Number(params.get('periodDays')) : undefined;
  const studentId = params.get('studentId') || undefined;
  const filters: PendingFilters = { connectionRef, courseId, studentId, type, level, periodDays, page };

  const courses = useQuery({
    queryKey: ['portal', 'pending', 'courses', connectionRef],
    queryFn: () => coursesGateway.list(connectionRef, 1, 100),
    enabled: Boolean(connectionRef),
    staleTime: 60_000,
  });
  const query = useQuery({
    queryKey: ['portal', 'pending', filters],
    queryFn: () => pendingGateway.list(filters),
    enabled: Boolean(connectionRef && courseId),
  });
  const totalPages = query.data?.meta.total !== undefined && query.data.meta.total !== null
    ? Math.max(1, Math.ceil(query.data.meta.total / query.data.meta.pageSize))
    : query.data?.meta.hasMore ? page + 1 : page;

  const update = (key: string, value: string) => {
    setParams((current) => {
      const next = new URLSearchParams(current);
      if (value) next.set(key, value);
      else next.delete(key);
      next.delete('page');
      return next;
    });
  };

  return (
    <main className="content-frame space-y-6">
      <header className="page-heading">
        <div><p className="eyebrow">OPERACIONAL</p><h1>Pendências</h1><p>Itens determinísticos para acompanhamento manual, sempre em modo somente leitura.</p></div>
        {query.data && <span className="freshness">Atualizado em {new Date(query.data.meta.generatedAt).toLocaleString('pt-BR')}</span>}
      </header>

      <Card>
        <CardHeader className="pb-4"><CardTitle className="flex items-center gap-2 text-lg"><BookOpen className="h-5 w-5 text-primary" /> Escopo e filtros</CardTitle><CardDescription>A consulta é feita em um único curso Moodle por vez.</CardDescription></CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-2 lg:grid-cols-4">
          <label className="grid gap-1.5 text-sm font-medium md:col-span-2" htmlFor="pending-course">Curso<select id="pending-course" aria-label="Curso" className="flex h-10 rounded-md border border-input bg-background px-3 py-2 text-sm" value={courseId ?? ''} onChange={(event) => update('courseId', event.target.value)} disabled={!connectionRef || courses.isPending}><option value="">{courses.isPending ? 'Carregando cursos…' : 'Selecione um curso'}</option>{courses.data?.data.map((course) => <option key={course.courseId} value={course.courseId}>{course.displayName ?? course.fullName} ({course.courseId})</option>)}</select></label>
          <div className="rounded-md bg-muted px-3 py-2 text-sm text-muted-foreground">Moodle: <span className="font-medium text-foreground">{connectionDisplayName(selectedConnection)}</span></div>
          <label className="grid gap-1.5 text-sm font-medium" htmlFor="pending-type">Tipo<select id="pending-type" aria-label="Tipo" className="flex h-10 rounded-md border border-input bg-background px-3 py-2 text-sm" value={type ?? ''} onChange={(event) => update('type', event.target.value)}><option value="">Todos</option>{Object.entries(labels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
          <label className="grid gap-1.5 text-sm font-medium" htmlFor="pending-level">Nível<select id="pending-level" aria-label="Nível" className="flex h-10 rounded-md border border-input bg-background px-3 py-2 text-sm" value={level ?? ''} onChange={(event) => update('level', event.target.value)}><option value="">Todos</option>{Object.entries(levelLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
          <label className="grid gap-1.5 text-sm font-medium" htmlFor="pending-period">Inatividade<select id="pending-period" aria-label="Período" className="flex h-10 rounded-md border border-input bg-background px-3 py-2 text-sm" value={periodDays ?? ''} onChange={(event) => update('periodDays', event.target.value)}><option value="">Padrão: 14 dias</option><option value="7">7 dias</option><option value="30">30 dias</option></select></label>
          {courses.isError && <p role="alert" className="text-sm text-destructive md:col-span-2">Não foi possível carregar os cursos desta conexão.</p>}
        </CardContent>
      </Card>

      {!courseId && <Card className="border-dashed"><CardContent className="flex flex-col items-center gap-3 py-12 text-center"><AlertTriangle className="h-8 w-8 text-muted-foreground" /><div><h2 className="font-semibold">Selecione um curso</h2><p className="mt-1 text-sm text-muted-foreground">O Portal não executa consulta agregada entre conexões ou cursos.</p></div></CardContent></Card>}
      {courseId && query.isPending && <div className="grid gap-4 md:grid-cols-2"><Skeleton className="h-40" /><Skeleton className="h-40" /></div>}
      {courseId && query.isError && <Card><CardContent className="py-8"><p role="alert" className="text-destructive">Não foi possível carregar as pendências.</p></CardContent></Card>}
      {courseId && query.isSuccess && query.data.data.length === 0 && <Card className="border-dashed"><CardContent className="flex flex-col items-center gap-3 py-12 text-center"><Clock3 className="h-8 w-8 text-muted-foreground" /><p className="text-sm text-muted-foreground">Nenhuma pendência encontrada neste curso.</p></CardContent></Card>}
      {courseId && query.isSuccess && query.data.data.length > 0 && <>
        <section className="grid gap-4 md:grid-cols-2" aria-label="Lista de pendências">
          {query.data.data.map((item) => <Card key={`${item.connectionRef}:${item.courseId}:${item.studentId}:${item.activityId ?? item.type}`} className="card-interactive"><CardHeader className="pb-3"><div className="flex items-start justify-between gap-3"><div><CardTitle className="text-base">{labels[item.type] ?? item.type}</CardTitle><CardDescription>{item.studentName} · {item.activityName}</CardDescription></div><Badge className={levelClasses[item.level]}>{levelLabels[item.level] ?? item.level}</Badge></div></CardHeader><CardContent className="space-y-4"><p className="text-sm text-muted-foreground">{item.factors.join(' ') || 'Sem fator adicional informado.'}</p>{item.type === 'awaiting_grading' && <p className="rounded-md bg-muted px-3 py-2 text-xs text-muted-foreground">Somente leitura: visualizar, filtrar ou abrir no Moodle. Nenhuma correção é executada pelo Portal.</p>}<div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3 text-xs text-muted-foreground"><span>{item.connectionRef} · {item.courseId}</span><Button variant="ghost" size="sm" asChild><Link to={`/alunos/${encodeURIComponent(item.connectionRef)}/${encodeURIComponent(item.courseId)}/${encodeURIComponent(item.studentId)}`}>Abrir aluno <ArrowRight className="h-4 w-4" /></Link></Button>{item.moodleUrl && <a className="text-primary hover:underline" href={item.moodleUrl} target="_blank" rel="noreferrer">Abrir no Moodle</a>}</div></CardContent></Card>)}
        </section>
        <Pagination page={page} totalPages={totalPages} onPageChange={(nextPage) => setParams((current) => { const next = new URLSearchParams(current); next.set('page', String(nextPage)); return next; })} />
      </>}
    </main>
  );
}
