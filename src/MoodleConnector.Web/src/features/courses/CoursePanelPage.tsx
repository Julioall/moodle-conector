import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { AlertTriangle, ArrowLeft, CheckCircle2, ClipboardList, ExternalLink, FileCheck2, MessageCircle, Search, Users } from 'lucide-react';
import { Link, useParams, useSearchParams } from 'react-router-dom';

import { Badge } from '../../components/ui/badge';
import { Button } from '../../components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card';
import { Input } from '../../components/ui/input';
import { Skeleton } from '../../components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../../components/ui/tabs';
import { dashboardGateway } from '../dashboard/dashboard-gateway';
import { followupGateway } from '../followup/followup-gateway';
import { studentsGateway, type Student } from '../students/students-gateway';
import { CourseFollowupDialog } from './CourseFollowupDialog';
import { coursesGateway } from './courses-gateway';

function formatDate(value?: string) {
  if (!value) return 'Não informado';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Não informado' : date.toLocaleDateString('pt-BR');
}

function formatDateTime(value?: string) {
  if (!value) return 'Não informado';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Não informado' : date.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

function isAtRisk(risk?: string) {
  return Boolean(risk && risk.trim().toLowerCase() !== 'normal');
}

function riskRank(risk?: string) {
  const normalized = risk?.trim().toLowerCase();
  return normalized === 'risco' ? 0 : normalized === 'atencao' ? 1 : 2;
}

function riskLabel(risk?: string) {
  const normalized = risk?.trim().toLowerCase();
  return normalized === 'risco' ? 'Em risco' : normalized === 'atencao' ? 'Atenção' : 'Normal';
}

const reasonLabels: Record<string, string> = { falta_acesso: 'Falta de acesso', atividade_pendente: 'Atividade pendente', desempenho: 'Desempenho', participacao: 'Participação', duvida: 'Dúvida', outro: 'Outro' };
const actionLabels: Record<string, string> = { mensagem: 'Mensagem', ligacao: 'Ligação', orientacao: 'Orientação', conversa_presencial: 'Conversa presencial', verificacao: 'Verificação', outro: 'Outro' };
const statusLabels: Record<string, string> = { em_acompanhamento: 'Em acompanhamento', aguardando_aluno: 'Aguardando aluno', resolvido: 'Resolvido' };

export function CoursePanelPage() {
  const { connectionRef = '', courseId = '' } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedTab = searchParams.get('tab') || 'overview';
  const activeTab = requestedTab === 'corrections' ? 'activities' : ['overview', 'students', 'activities'].includes(requestedTab) ? requestedTab : 'overview';
  const [studentsPage, setStudentsPage] = useState(1);
  const [activitiesPage, setActivitiesPage] = useState(1);
  const [studentSearch, setStudentSearch] = useState('');
  const [studentRiskFilter, setStudentRiskFilter] = useState('todos');
  const [activityFilter, setActivityFilter] = useState<'pending' | 'all'>('pending');
  const [followupOpen, setFollowupOpen] = useState(false);
  const enabled = Boolean(connectionRef && courseId);

  const course = useQuery({ queryKey: ['app', 'course', connectionRef, courseId], queryFn: () => coursesGateway.get(connectionRef, courseId), enabled, staleTime: 60_000 });
  const dashboard = useQuery({ queryKey: ['app', 'course-dashboard', connectionRef, courseId], queryFn: () => dashboardGateway.get(connectionRef, courseId), enabled, staleTime: 30_000 });
  const followups = useQuery({ queryKey: ['app', 'followups', connectionRef, courseId], queryFn: () => followupGateway.list({ connectionRef, courseId }), enabled: enabled && activeTab === 'overview', staleTime: 30_000 });
  const activities = useQuery({
    queryKey: ['app', 'course-activities', connectionRef, courseId, activitiesPage, activeTab === 'activities'],
    queryFn: () => coursesGateway.activities(connectionRef, courseId, activitiesPage, 20, activeTab === 'activities'),
    enabled: enabled && (activeTab === 'overview' || activeTab === 'activities'),
    staleTime: 30_000,
  });
  const students = useQuery({
    queryKey: ['app', 'course-students', connectionRef, courseId, studentsPage],
    queryFn: () => studentsGateway.byCourse(connectionRef, courseId, studentsPage, 25, true),
    enabled: enabled && (activeTab === 'students' || followupOpen),
    staleTime: 30_000,
  });

  const data = course.data?.data;
  const summary = dashboard.data?.data.summary;
  const studentCount = summary?.activeStudents ?? undefined;
  const activityCount = activities.data?.meta.total ?? activities.data?.data.length ?? 0;
  const studentTotalPages = Math.max(1, students.data?.meta.total ? Math.ceil(students.data.meta.total / 25) : students.data?.meta.hasMore ? studentsPage + 1 : studentsPage);
  const activityTotalPages = Math.max(1, Math.ceil(activityCount / 20));
  const visibleStudents = useMemo(() => {
    const normalizedSearch = studentSearch.trim().toLocaleLowerCase();
    return [...(students.data?.data ?? [])]
      .filter((student) => {
        const matchesSearch = !normalizedSearch || `${student.name} ${student.email ?? ''}`.toLocaleLowerCase().includes(normalizedSearch);
        const normalizedRisk = student.risk?.trim().toLowerCase() ?? 'normal';
        const matchesRisk = studentRiskFilter === 'todos' || normalizedRisk === studentRiskFilter;
        return matchesSearch && matchesRisk;
      })
      .sort((left, right) => riskRank(left.risk) - riskRank(right.risk) || left.name.localeCompare(right.name, 'pt-BR'));
  }, [students.data?.data, studentRiskFilter, studentSearch]);
  const pendingActivities = (activities.data?.data ?? []).filter((activity) => (activity.pendingSubmissionCount ?? 0) + (activity.awaitingGradingCount ?? 0) > 0);

  const handleTabChange = (value: string) => {
    setSearchParams((current) => {
      const next = new URLSearchParams(current);
      if (value === 'overview') next.delete('tab');
      else next.set('tab', value);
      return next;
    }, { replace: true });
  };

  return (
    <main className="space-y-6 animate-fade-in" aria-labelledby="course-title">
      {course.isPending && <Card><CardContent className="space-y-4 py-8"><Skeleton className="h-8 w-2/3" /><Skeleton className="h-4 w-1/3" /></CardContent></Card>}
      {course.isError && <Card><CardContent className="py-8"><p role="alert" className="text-destructive">Não foi possível carregar o curso.</p></CardContent></Card>}
      {!course.isPending && !course.isError && !data && <Card><CardContent className="py-8"><p>Curso não encontrado nesta conexão.</p></CardContent></Card>}

      {data && <>
        <header className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
          <div className="flex items-start gap-2"><Button variant="ghost" size="icon" asChild className="mt-0.5 h-8 w-8 shrink-0"><Link to={`/meus-cursos${connectionRef ? `?connectionRef=${encodeURIComponent(connectionRef)}` : ''}`} aria-label="Voltar para meus cursos"><ArrowLeft className="h-4 w-4" /></Link></Button><div><p className="eyebrow">CURSO · {connectionRef}</p><h1 id="course-title" className="line-clamp-2 text-2xl font-bold tracking-tight">{data.displayName ?? data.fullName}</h1><p className="mt-1 text-sm text-muted-foreground">{data.shortName ?? data.categoryName ?? 'Acompanhamento da turma'}</p></div></div>
          <div className="flex flex-wrap items-center gap-3 md:justify-end"><div className="hidden text-right text-xs text-muted-foreground sm:block"><span>Última consulta:</span><span className="ml-1 font-medium">{formatDateTime(course.data?.meta.generatedAt)}</span></div><Badge variant="outline">Somente leitura</Badge>{data.viewUrl && <Button variant="outline" size="sm" asChild><a href={data.viewUrl} target="_blank" rel="noreferrer">Abrir no Moodle <ExternalLink className="h-4 w-4" /></a></Button>}</div>
        </header>

        <section aria-labelledby="course-state-title" className="space-y-3"><div className="flex flex-wrap items-center justify-between gap-3"><div><h2 id="course-state-title" className="text-lg font-semibold">Estado da turma</h2><p className="text-sm text-muted-foreground">Indicadores para decidir onde agir primeiro.</p></div><Button variant="outline" size="sm" onClick={() => setFollowupOpen(true)}><MessageCircle className="h-4 w-4" />Registrar acompanhamento</Button></div><div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <Card><CardContent className="p-4"><div className="flex items-center gap-3"><div className="rounded-lg bg-primary/10 p-2"><Users className="h-5 w-5 text-primary" /></div><div><p className="text-2xl font-bold">{studentCount ?? '—'}</p><p className="text-xs text-muted-foreground">Alunos ativos</p></div></div></CardContent></Card>
          <Card><CardContent className="p-4"><div className="flex items-center gap-3"><div className="rounded-lg bg-risk-risco/10 p-2"><AlertTriangle className="h-5 w-5 text-risk-risco" /></div><div><p className="text-2xl font-bold">{summary?.studentsAtRisk ?? '—'}</p><p className="text-xs text-muted-foreground">Alunos em risco</p></div></div></CardContent></Card>
          <Card><CardContent className="p-4"><div className="flex items-center gap-3"><div className="rounded-lg bg-amber-500/10 p-2"><ClipboardList className="h-5 w-5 text-amber-600" /></div><div><p className="text-2xl font-bold">{summary?.pendingSubmissionAssignments ?? '—'}</p><p className="text-xs text-muted-foreground">Entregas pendentes</p></div></div></CardContent></Card>
          <Card><CardContent className="p-4"><div className="flex items-center gap-3"><div className="rounded-lg bg-green-500/10 p-2"><FileCheck2 className="h-5 w-5 text-green-600" /></div><div><p className="text-2xl font-bold">{summary?.pendingCorrectionAssignments ?? '—'}</p><p className="text-xs text-muted-foreground">Aguardando correção</p></div></div></CardContent></Card>
        </div></section>

        <Tabs value={activeTab} onValueChange={handleTabChange} className="space-y-4"><TabsList className="h-auto w-full flex-wrap justify-start gap-1" aria-label="Seções do curso"><TabsTrigger value="overview">Visão geral</TabsTrigger><TabsTrigger value="students">Alunos{studentCount == null ? '' : ` (${studentCount})`}</TabsTrigger><TabsTrigger value="activities">Atividades{activityCount ? ` (${activityCount})` : ''}</TabsTrigger></TabsList>
          <TabsContent value="overview" className="space-y-4">
            <Card><CardHeader><div className="flex flex-wrap items-start justify-between gap-3"><div><CardTitle className="text-base">Prioridades</CardTitle><CardDescription>Sinais que pedem uma decisão do tutor.</CardDescription></div>{summary && <Badge variant={summary.studentsAtRisk > 0 || (summary.pendingCorrectionAssignments ?? 0) > 0 ? 'destructive' : 'outline'}>{summary.studentsAtRisk + (summary.pendingCorrectionAssignments ?? 0)} {summary.studentsAtRisk + (summary.pendingCorrectionAssignments ?? 0) === 1 ? 'ação sugerida' : 'ações sugeridas'}</Badge>}</div></CardHeader><CardContent>{dashboard.isPending && <div className="space-y-3"><Skeleton className="h-10 w-full" /><Skeleton className="h-10 w-full" /></div>}{dashboard.isError && <p role="alert" className="text-sm text-destructive">Não foi possível carregar o estado da turma.</p>}{dashboard.isSuccess && summary && summary.studentsAtRisk === 0 && (summary.pendingSubmissionAssignments ?? 0) === 0 && (summary.pendingCorrectionAssignments ?? 0) === 0 && <div className="flex items-center gap-3 rounded-lg border border-dashed p-4 text-sm text-muted-foreground"><CheckCircle2 className="h-5 w-5 text-green-600" />Nenhuma prioridade operacional identificada.</div>}{dashboard.isSuccess && dashboard.data.data.priorities.length > 0 && <div className="divide-y">{dashboard.data.data.priorities.slice(0, 8).map((item) => <div key={item.key} className="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-0"><div className="flex items-start gap-3"><div className="mt-0.5 rounded-full bg-muted p-1.5">{item.level === 'risk' ? <AlertTriangle className="h-4 w-4 text-risk-risco" /> : <ClipboardList className="h-4 w-4 text-amber-600" />}</div><div><p className="font-medium">{item.title}</p><p className="text-sm text-muted-foreground">{item.detail}</p></div></div>{item.studentId && <Button variant="outline" size="sm" asChild><Link to={`/cursos/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}/alunos/${encodeURIComponent(item.studentId)}`}>Ver aluno</Link></Button>}</div>)}</div>}</CardContent></Card>
            <Card><CardHeader><div className="flex flex-wrap items-center justify-between gap-3"><div><CardTitle className="text-base">Histórico de acompanhamento</CardTitle><CardDescription>Intervenções realizadas por tutores, monitores e analistas nesta turma.</CardDescription></div>{followups.data && <Badge variant="outline">{followups.data.data.length} {followups.data.data.length === 1 ? 'registro' : 'registros'}</Badge>}</div></CardHeader><CardContent>{followups.isPending && <div className="space-y-3"><Skeleton className="h-14 w-full" /><Skeleton className="h-14 w-full" /></div>}{followups.isError && <p role="alert" className="text-sm text-destructive">Não foi possível carregar o histórico de acompanhamento.</p>}{followups.isSuccess && followups.data.data.length === 0 && <div className="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground">Nenhuma intervenção registrada nesta turma.</div>}{followups.isSuccess && followups.data.data.length > 0 && <div className="divide-y">{followups.data.data.slice(0, 8).map((record) => <article key={record.id} className="py-4 first:pt-0"><div className="flex flex-wrap items-start justify-between gap-3"><div><div className="flex flex-wrap items-center gap-2"><p className="font-medium">{record.studentName ?? record.studentRef}</p><Badge variant="secondary">{actionLabels[record.action ?? ''] ?? 'Acompanhamento'}</Badge>{record.status && <Badge variant="outline">{statusLabels[record.status] ?? record.status}</Badge>}</div><p className="text-xs text-muted-foreground">{record.reason ? reasonLabels[record.reason] ?? record.reason : 'Acompanhamento'} · por {record.actorName ?? 'Usuário'} · {formatDateTime(record.occurredAt)}</p></div></div><p className="mt-2 whitespace-pre-wrap text-sm text-muted-foreground">{record.notes}</p></article>)}</div>}{followups.isSuccess && followups.data.meta.hasMore && <p className="mt-3 text-xs text-muted-foreground">Mostrando os 8 registros mais recentes.</p>}</CardContent></Card>
          </TabsContent>
          <TabsContent value="students"><Card><CardHeader><div className="flex flex-wrap items-start justify-between gap-3"><div><CardTitle className="text-base">Alunos do curso</CardTitle><CardDescription>Lista operacional com risco, último acesso e pendências.</CardDescription></div><Button size="sm" onClick={() => setFollowupOpen(true)}><MessageCircle className="h-4 w-4" />Registrar acompanhamento</Button></div></CardHeader><CardContent>
            <div className="mb-4 grid gap-3 md:grid-cols-[minmax(0,1fr)_auto]"><label className="relative"><span className="sr-only">Buscar aluno</span><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" /><Input className="pl-9" value={studentSearch} onChange={(event) => setStudentSearch(event.target.value)} placeholder="Buscar aluno ou e-mail" /></label><div className="flex flex-wrap gap-2" role="group" aria-label="Filtrar alunos por situação">{[['todos', 'Todos'], ['risco', 'Risco'], ['atencao', 'Atenção'], ['normal', 'Normal']].map(([value, label]) => <Button key={value} type="button" size="sm" variant={studentRiskFilter === value ? 'default' : 'outline'} onClick={() => setStudentRiskFilter(value)}>{label}</Button>)}</div></div>
            {students.isPending && <div className="space-y-3"><Skeleton className="h-10 w-full" /><Skeleton className="h-10 w-full" /></div>}{students.isError && <p role="alert" className="text-destructive">Não foi possível carregar os alunos.</p>}{students.isSuccess && visibleStudents.length === 0 && <p className="py-8 text-center text-sm text-muted-foreground">Nenhum aluno corresponde aos filtros atuais.</p>}{students.isSuccess && visibleStudents.length > 0 && <div className="divide-y">{visibleStudents.map((student: Student) => <div key={`${student.connectionRef}:${student.studentId}`} className="grid gap-3 py-4 first:pt-0 md:grid-cols-[minmax(0,1fr)_150px_140px_120px_auto] md:items-center"><div><p className="font-medium">{student.name}</p><p className="text-xs text-muted-foreground">{student.email ?? 'E-mail não informado'}</p></div><div><p className="text-xs text-muted-foreground">Situação</p><Badge variant={isAtRisk(student.risk) ? 'destructive' : 'secondary'}>{riskLabel(student.risk)}</Badge></div><div><p className="text-xs text-muted-foreground">Último acesso</p><p className="text-sm">{student.lastCourseAccessAt ? formatDateTime(student.lastCourseAccessAt) : 'Nunca acessou'}</p></div><div><p className="text-xs text-muted-foreground">Pendências</p><p className="text-sm font-medium">{student.pendingCount ?? 0}</p></div><Button variant="outline" size="sm" asChild><Link to={`/cursos/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}/alunos/${encodeURIComponent(student.studentId)}`}>Abrir perfil</Link></Button></div>)}</div>}{studentTotalPages > 1 && <div className="mt-4 flex items-center justify-between gap-3 border-t pt-4 text-xs text-muted-foreground"><span>Página {studentsPage} de {studentTotalPages}</span><div className="flex gap-2"><Button variant="outline" size="sm" onClick={() => setStudentsPage((page) => Math.max(1, page - 1))} disabled={studentsPage <= 1}>Anterior</Button><Button variant="outline" size="sm" onClick={() => setStudentsPage((page) => Math.min(studentTotalPages, page + 1))} disabled={studentsPage >= studentTotalPages}>Próxima</Button></div></div>}
          </CardContent></Card></TabsContent>
          <TabsContent value="activities"><Card><CardHeader><div className="flex flex-wrap items-start justify-between gap-3"><div><CardTitle className="text-base">Atividades</CardTitle><CardDescription>Acompanhe as atividades que exigem ação e abra o detalhe no Moodle.</CardDescription></div><div className="flex flex-wrap items-center gap-2">{summary && (summary.pendingCorrectionAssignments ?? 0) > 0 && <Badge variant="destructive">{summary.pendingCorrectionAssignments} para corrigir</Badge>}<div className="flex flex-wrap gap-2" role="group" aria-label="Filtrar atividades"><Button type="button" size="sm" variant={activityFilter === 'pending' ? 'default' : 'outline'} onClick={() => setActivityFilter('pending')}>Com pendência</Button><Button type="button" size="sm" variant={activityFilter === 'all' ? 'default' : 'outline'} onClick={() => setActivityFilter('all')}>Todas</Button></div></div></div></CardHeader><CardContent>{activities.isPending && <div className="space-y-3"><Skeleton className="h-12 w-full" /><Skeleton className="h-12 w-full" /></div>}{activities.isError && <p role="alert" className="text-destructive">Não foi possível carregar as atividades.</p>}{activities.isSuccess && activityFilter === 'pending' && pendingActivities.length === 0 && <div className="rounded-lg border border-dashed p-6 text-center"><CheckCircle2 className="mx-auto h-6 w-6 text-green-600" /><p className="mt-2 text-sm font-medium">Nenhuma atividade pendente nesta página.</p><Button className="mt-3" variant="outline" size="sm" onClick={() => setActivityFilter('all')}>Mostrar todas</Button></div>}{activities.isSuccess && (activityFilter === 'all' ? activities.data.data : pendingActivities).length > 0 && <div className="divide-y">{(activityFilter === 'all' ? activities.data.data : pendingActivities).map((activity) => { const pending = activity.pendingSubmissionCount ?? 0; const grading = activity.awaitingGradingCount ?? 0; return <article className="flex flex-wrap items-center justify-between gap-3 py-4 first:pt-0" key={`${activity.connectionRef}:${activity.courseId}:${activity.activityId}`}><div className="min-w-0"><div className="flex flex-wrap items-center gap-2"><p className="font-medium">{activity.name}</p>{pending > 0 && <Badge variant="secondary">{pending} entrega{pending === 1 ? '' : 's'} pendente{pending === 1 ? '' : 's'}</Badge>}{grading > 0 && <Badge variant="destructive">{grading} para corrigir</Badge>}</div><p className="text-xs text-muted-foreground">{activity.activityType}{activity.dueAt ? ` · prazo ${formatDate(activity.dueAt)}` : ' · sem prazo'}</p></div>{activity.url && <a className="flex shrink-0 items-center gap-1 text-sm text-primary hover:underline" href={activity.url} target="_blank" rel="noreferrer">Abrir no Moodle <ExternalLink className="h-3.5 w-3.5" /></a>}</article>; })}</div>}{activityTotalPages > 1 && <div className="mt-4 flex items-center justify-between gap-3 border-t pt-4 text-xs text-muted-foreground"><span>Página {activitiesPage} de {activityTotalPages}</span><div className="flex gap-2"><Button variant="outline" size="sm" onClick={() => setActivitiesPage((page) => Math.max(1, page - 1))} disabled={activitiesPage <= 1}>Anterior</Button><Button variant="outline" size="sm" onClick={() => setActivitiesPage((page) => Math.min(activityTotalPages, page + 1))} disabled={activitiesPage >= activityTotalPages}>Próxima</Button></div></div>}</CardContent></Card></TabsContent>
        </Tabs>
        <CourseFollowupDialog open={followupOpen} onOpenChange={setFollowupOpen} connectionRef={connectionRef} courseId={courseId} students={students.data?.data ?? []} />
      </>}
    </main>
  );
}
