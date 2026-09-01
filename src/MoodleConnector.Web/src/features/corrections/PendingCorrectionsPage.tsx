import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AlertTriangle, CheckCircle2, ChevronLeft, ChevronRight, ClipboardCheck, ExternalLink, FileText, History, RefreshCw, Send, ShieldCheck } from 'lucide-react';
import { useSearchParams } from 'react-router-dom';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Textarea } from '@/components/ui/textarea';
import { useSession } from '@/features/auth/useSession';
import { useConnectionScope } from '@/features/connections/useConnectionScope';
import { coursesGateway, type Activity } from '@/features/courses/courses-gateway';
import { studentsGateway } from '@/features/students/students-gateway';
import { evidenceGateway, pendingGateway, submissionsGateway, type PendingItem, type Submission } from './submissions-gateway';

export type CourseContext = { connectionRef: string; courseId: string; courseName?: string };

function formatDate(value?: string) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleDateString('pt-BR');
}

function formatDateTime(value?: string) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString('pt-BR');
}

function formatGrade(value?: number, maximum?: number) {
  if (value == null) return 'Sem nota';
  return maximum != null ? `${value} / ${maximum}` : String(value);
}

function activityIsAssignment(activity: Activity) {
  return activity.activityType.toLowerCase() === 'assign' || activity.activityType.toLowerCase().includes('assignment');
}

function PendingRow({ item }: { item: PendingItem }) {
  return <TableRow>
    <TableCell><div className="font-medium">{item.studentName}</div><div className="text-xs text-muted-foreground">{item.studentId}</div></TableCell>
    <TableCell><div className="font-medium">{item.activityName}</div><div className="text-xs text-muted-foreground">{item.factors.join(' · ')}</div></TableCell>
    <TableCell><Badge variant={item.level === 'critical' ? 'destructive' : 'outline'}>{item.type === 'pending_submission' ? 'Sem entrega' : 'Acesso'}</Badge></TableCell>
    <TableCell className="text-sm text-muted-foreground">{formatDate(item.dueAt)}</TableCell>
    <TableCell className="text-right">{item.moodleUrl && <a className="inline-flex items-center gap-1 text-sm text-primary hover:underline" href={item.moodleUrl} target="_blank" rel="noreferrer">Moodle <ExternalLink className="h-3.5 w-3.5" /></a>}</TableCell>
  </TableRow>;
}

export function PendingCorrectionsPage({ courseContext, embedded = false, compact = false }: { courseContext?: CourseContext; embedded?: boolean; compact?: boolean }) {
  const { can } = useSession();
  const canManage = can('grading.manage');
  const { connectionRef, selectedConnection } = useConnectionScope();
  const [params, setParams] = useSearchParams();
  const [selectedCourseId, setSelectedCourseId] = useState(params.get('courseId') ?? '');
  const [assignmentId, setAssignmentId] = useState(params.get('assignmentId') ?? '');
  const [submissionPage, setSubmissionPage] = useState(1);
  const [selectedSubmission, setSelectedSubmission] = useState<Submission | null>(null);
  const [gradeOpen, setGradeOpen] = useState(false);
  const [grade, setGrade] = useState('');
  const [feedback, setFeedback] = useState('');
  const [justification, setJustification] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [prepareResult, setPrepareResult] = useState<{ pendingActionId: string; preview: { studentFullName: string; proposedGrade: number; previousGrade?: number; confirmationText: string; risks: string[]; expiresAt: string } } | null>(null);
  const client = useQueryClient();
  const courseId = courseContext?.courseId ?? selectedCourseId;
  const effectiveConnectionRef = courseContext?.connectionRef ?? connectionRef;
  const titleId = embedded ? 'course-pending-title' : 'pending-title';
  const Container = embedded ? 'section' : 'main';

  const courses = useQuery({ queryKey: ['app', 'corrections', 'courses', connectionRef], queryFn: () => coursesGateway.list(connectionRef, 1, 100), enabled: Boolean(connectionRef && !courseContext), staleTime: 60_000 });
  const activities = useQuery({ queryKey: ['app', 'corrections', 'activities', effectiveConnectionRef, courseId], queryFn: () => coursesGateway.activities(effectiveConnectionRef!, courseId, 1, 100, true), enabled: Boolean(effectiveConnectionRef && courseId), staleTime: 60_000, refetchInterval: (query) => query.state.data?.meta.source === 'background' || query.state.data?.meta.refreshQueued ? 2_000 : false });
  const students = useQuery({ queryKey: ['app', 'corrections', 'students', effectiveConnectionRef, courseId], queryFn: () => studentsGateway.byCourse(effectiveConnectionRef!, courseId, 1, 100), enabled: Boolean(effectiveConnectionRef && courseId), staleTime: 60_000 });
  const pending = useQuery({ queryKey: ['app', 'pending', effectiveConnectionRef, courseId], queryFn: () => pendingGateway.list(effectiveConnectionRef!, courseId), enabled: Boolean(effectiveConnectionRef && courseId && !compact), staleTime: 20_000, refetchInterval: (query) => query.state.data?.meta.source === 'background' || query.state.data?.meta.refreshQueued || query.state.data?.meta.complete === false ? 2_000 : false });
  const evidence = useQuery({ queryKey: ['app', 'evidence', effectiveConnectionRef, courseId], queryFn: () => evidenceGateway.list(effectiveConnectionRef!, courseId), enabled: Boolean(effectiveConnectionRef && courseId && !compact), staleTime: 20_000 });
  const submissions = useQuery({ queryKey: ['app', 'submissions', effectiveConnectionRef, courseId, assignmentId, submissionPage], queryFn: () => submissionsGateway.list(effectiveConnectionRef!, courseId, assignmentId, 'awaiting_grading', submissionPage, 25), enabled: Boolean(effectiveConnectionRef && courseId && assignmentId), staleTime: 20_000, refetchInterval: (query) => query.state.data?.meta.source === 'background' || query.state.data?.meta.refreshQueued || query.state.data?.meta.complete === false ? 2_000 : false });
  const submissionDetail = useQuery({ queryKey: ['app', 'submission-detail', effectiveConnectionRef, courseId, assignmentId, selectedSubmission?.userId], queryFn: () => submissionsGateway.detail(effectiveConnectionRef!, courseId, assignmentId, selectedSubmission!.userId), enabled: Boolean(gradeOpen && effectiveConnectionRef && courseId && assignmentId && selectedSubmission), staleTime: 20_000 });

  const assignmentOptions = useMemo(() => (activities.data?.data ?? []).filter(activityIsAssignment), [activities.data?.data]);
  const submitPrepare = useMutation({
    mutationFn: () => submissionsGateway.prepareGrade({ connectionRef: effectiveConnectionRef!, courseId, assignmentId, studentId: selectedSubmission!.userId, proposedGrade: Number(grade), feedbackText: feedback.trim() || undefined, justificationText: justification.trim() }),
    onSuccess: (response) => { setPrepareResult(response.data); setConfirmation(''); },
  });
  const confirmGrade = useMutation({
    mutationFn: () => submissionsGateway.confirmGrade({ connectionRef: effectiveConnectionRef!, pendingActionId: prepareResult!.pendingActionId, confirmationText: confirmation }),
    onSuccess: () => { setGradeOpen(false); setPrepareResult(null); setSelectedSubmission(null); setGrade(''); setFeedback(''); setJustification(''); setConfirmation(''); void client.invalidateQueries({ queryKey: ['app', 'submissions', effectiveConnectionRef, courseId, assignmentId] }); void client.invalidateQueries({ queryKey: ['app', 'pending', effectiveConnectionRef, courseId] }); },
  });

  function setScope(nextCourseId: string, nextAssignmentId = '') {
    if (!courseContext) setSelectedCourseId(nextCourseId);
    setAssignmentId(nextAssignmentId);
    setSubmissionPage(1);
    setParams((current) => { const next = new URLSearchParams(current); if (nextCourseId) next.set('courseId', nextCourseId); else next.delete('courseId'); if (nextAssignmentId) next.set('assignmentId', nextAssignmentId); else next.delete('assignmentId'); return next; }, { replace: true });
  }

  function openGrade(submission: Submission) {
    setSelectedSubmission(submission);
    setPrepareResult(null);
    setConfirmation('');
    setGrade('');
    setFeedback('');
    setJustification('');
    setGradeOpen(true);
  }

  function refreshCorrections() {
    if (!effectiveConnectionRef || !courseId) return;
    void client.fetchQuery({
      queryKey: ['app', 'corrections', 'activities', effectiveConnectionRef, courseId],
      queryFn: () => coursesGateway.activities(effectiveConnectionRef, courseId, 1, 100, true, true),
      staleTime: 0,
    });
    if (!compact) {
      void client.fetchQuery({
        queryKey: ['app', 'pending', effectiveConnectionRef, courseId],
        queryFn: () => pendingGateway.list(effectiveConnectionRef, courseId, true),
        staleTime: 0,
      });
    }
    void client.fetchQuery({
      queryKey: ['app', 'corrections', 'students', effectiveConnectionRef, courseId],
      queryFn: () => studentsGateway.byCourse(effectiveConnectionRef, courseId, 1, 100, false, true),
      staleTime: 0,
    });
    if (assignmentId) {
      void client.fetchQuery({
        queryKey: ['app', 'submissions', effectiveConnectionRef, courseId, assignmentId, submissionPage],
        queryFn: () => submissionsGateway.list(effectiveConnectionRef, courseId, assignmentId, 'awaiting_grading', submissionPage, 25, true),
        staleTime: 0,
      });
    }
  }

  const pendingItems = pending.data?.data ?? [];
  const awaiting = submissions.data?.data.submissions ?? [];
  const activeSubmission = submissionDetail.data?.data ?? selectedSubmission;
  const activitiesPreparing = activities.data?.meta.source === 'background' || activities.data?.meta.complete === false;
  const pendingPreparing = pending.data?.meta.source === 'background' || pending.data?.meta.complete === false;
  const submissionsPreparing = submissions.data?.meta.source === 'background' || submissions.data?.meta.complete === false;
  const submissionTotal = submissions.data?.data.total ?? 0;
  const totalSubmissionPages = Math.max(1, Math.ceil(submissionTotal / 25));
  const studentsById = useMemo(() => new Map((students.data?.data ?? []).map((student) => [student.studentId, student])), [students.data?.data]);
  const correctionActivities = assignmentOptions.filter((activity) => (activity.awaitingGradingCount ?? 0) > 0);

  return <Container className={embedded ? 'space-y-4' : 'content-frame'} aria-labelledby={titleId}>
    <header className="page-heading"><div><p className="eyebrow">CORREÇÕES · MOODLE</p>{embedded ? <h2 id={titleId}>Correções pendentes</h2> : <h1 id={titleId}>Correções pendentes</h1>}<p>{compact ? 'Consulte a atividade, os alunos que aguardam correção, último acesso e nota atual.' : embedded ? `Sinais e submissões vinculados a ${courseContext?.courseName ?? `este curso (${courseId})`}.` : 'Consolide sinais do Moodle, submissões e correções pendentes em uma fila auditável.'}</p></div><div className="flex items-center gap-3"><span className="freshness">Escopo: {selectedConnection?.alias ?? connectionRef ?? courseContext?.connectionRef ?? 'Moodle padrão'}</span><Button type="button" variant="outline" onClick={refreshCorrections} disabled={activities.isFetching || students.isFetching || (!compact && pending.isFetching) || submissions.isFetching}><RefreshCw className={activities.isFetching || students.isFetching || (!compact && pending.isFetching) || submissions.isFetching ? 'animate-spin' : ''} />Atualizar</Button></div></header>

    <Card><CardHeader><CardTitle className="flex items-center gap-2 text-lg"><ClipboardCheck className="h-5 w-5 text-primary" />Atividades aguardando correção</CardTitle><CardDescription>{courseContext ? 'Escolha uma atividade para abrir os alunos que enviaram e ainda aguardam avaliação.' : 'Escolha um curso e uma atividade do Moodle para abrir a fila de submissões aguardando correção.'}</CardDescription></CardHeader><CardContent className="grid gap-4 md:grid-cols-2">{courseContext ? <div className="rounded-md border bg-muted/30 px-3 py-2 text-sm"><p className="text-xs text-muted-foreground">Curso atual</p><p className="mt-1 font-medium">{courseContext.courseName ?? courseId}</p></div> : <label className="grid gap-1.5 text-sm font-medium">Curso<select className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm" value={courseId} onChange={(event) => setScope(event.target.value)} disabled={!connectionRef || courses.isPending}><option value="">Selecione um curso</option>{(courses.data?.data ?? []).map((course) => <option key={course.courseId} value={course.courseId}>{course.displayName ?? course.fullName}</option>)}</select></label>}<label className="grid gap-1.5 text-sm font-medium">Atividade avaliativa<Select value={assignmentId || 'none'} onValueChange={(value) => setScope(courseId, value === 'none' ? '' : value)} disabled={!courseId || activities.isPending || activitiesPreparing}><SelectTrigger><SelectValue placeholder={activitiesPreparing ? 'Preparando atividades…' : 'Selecionar atividade'} /></SelectTrigger><SelectContent><SelectItem value="none">{activitiesPreparing ? 'Preparando atividades do Moodle…' : 'Selecionar atividade'}</SelectItem>{assignmentOptions.map((activity) => <SelectItem key={activity.activityId} value={activity.activityId}>{activity.name}{activity.awaitingGradingCount ? ` · ${activity.awaitingGradingCount} para corrigir` : ''}</SelectItem>)}</SelectContent></Select>{activitiesPreparing && <p className="text-xs text-muted-foreground" role="status">Preparando atividades do Moodle. A lista será aberta automaticamente.</p>}</label></CardContent></Card>

    {courseId && compact && <section aria-label="Resumo das atividades para corrigir"><div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">{activities.isPending ? <><Card><CardContent className="p-4 text-sm text-muted-foreground">Carregando atividades…</CardContent></Card><Card><CardContent className="p-4 text-sm text-muted-foreground">Calculando fila de correções…</CardContent></Card></> : correctionActivities.length === 0 ? <Card className="md:col-span-2 xl:col-span-3"><CardContent className="p-5 text-sm text-muted-foreground">Nenhuma atividade com correção pendente.</CardContent></Card> : correctionActivities.map((activity) => <Card key={activity.activityId} className={assignmentId === activity.activityId ? 'border-primary/50 bg-primary/5' : undefined}><CardContent className="flex items-center justify-between gap-3 p-4"><div className="min-w-0"><p className="truncate font-medium">{activity.name}</p><p className="mt-1 text-xs text-muted-foreground">{activity.awaitingGradingCount} aguardando correção{activity.dueAt ? ` · prazo ${formatDate(activity.dueAt)}` : ''}</p></div><Button size="sm" variant={assignmentId === activity.activityId ? 'secondary' : 'outline'} onClick={() => setScope(courseId, activity.activityId)}>Ver fila</Button></CardContent></Card>)}</div></section>}

    {!effectiveConnectionRef && <Card><CardContent className="p-8 text-sm text-muted-foreground">Configure uma conexão Moodle para consultar pendências.</CardContent></Card>}
    {courseId && !compact && <section className="space-y-4" aria-labelledby="signals-title"><div className="flex items-end justify-between gap-3"><div><h2 id="signals-title" className="text-lg font-semibold">Sinais de acompanhamento</h2><p className="text-sm text-muted-foreground">Entregas atrasadas, não entregues e falta de acesso recente.</p></div><Badge variant="outline">{pendingItems.length} itens</Badge></div><Card><CardContent className="p-0">{pending.isPending ? <p className="p-8 text-sm text-muted-foreground">Consultando Moodle…</p> : pending.isError ? <p className="p-8 text-sm text-destructive" role="alert">Não foi possível carregar as pendências.</p> : pendingPreparing ? <div className="p-8 text-sm text-muted-foreground" role="status">Preparando os sinais do Moodle em segundo plano. Esta seção será atualizada automaticamente.</div> : pendingItems.length === 0 ? <div className="p-8 text-sm text-muted-foreground">Nenhum sinal pendente para este curso.</div> : <Table><TableHeader><TableRow><TableHead>Estudante</TableHead><TableHead>Atividade / fator</TableHead><TableHead>Tipo</TableHead><TableHead>Prazo</TableHead><TableHead /></TableRow></TableHeader><TableBody>{pendingItems.map((item) => <PendingRow key={`${item.studentId}:${item.activityId}:${item.type}`} item={item} />)}</TableBody></Table>}</CardContent></Card></section>}

    {assignmentId && <section className="space-y-4" aria-labelledby="corrections-title">
      <div className="flex items-end justify-between gap-3"><div><h2 id="corrections-title" className="text-lg font-semibold">Alunos aguardando correção</h2><p className="text-sm text-muted-foreground">Veja a entrega, o último acesso e a nota atualmente registrada antes de corrigir.</p></div><Badge variant="secondary">{submissionTotal} estudantes</Badge></div>
      <Card><CardContent className="p-0">{submissions.isPending ? <p className="p-8 text-sm text-muted-foreground">Carregando submissões…</p> : submissions.isError ? <p className="p-8 text-sm text-destructive" role="alert">Não foi possível carregar as submissões.</p> : submissionsPreparing ? <div className="p-8 text-sm text-muted-foreground" role="status">Preparando a fila de correções no Moodle. Você não precisa recarregar a página.</div> : awaiting.length === 0 ? <div className="p-8 text-sm text-muted-foreground">Nenhuma submissão aguardando correção.</div> : <><Table><TableHeader><TableRow><TableHead>Estudante</TableHead><TableHead>Último acesso</TableHead><TableHead>Enviado em</TableHead><TableHead>Nota atual</TableHead><TableHead className="text-right">Ação</TableHead></TableRow></TableHeader><TableBody>{awaiting.map((submission) => { const student = studentsById.get(submission.userId); const lastAccess = student ? (student.lastCourseAccessAt ? formatDateTime(student.lastCourseAccessAt) : 'Nunca acessou') : 'Não informado'; return <TableRow key={submission.userId}><TableCell><div className="font-medium">{submission.fullName ?? submission.userId}</div><div className="text-xs text-muted-foreground">{submission.fileCount > 0 ? `${submission.fileCount} arquivo${submission.fileCount === 1 ? '' : 's'}` : submission.hasOnlineText ? 'Texto online' : 'Sem arquivo'}</div></TableCell><TableCell className="text-sm">{lastAccess}</TableCell><TableCell className="text-sm text-muted-foreground">{formatDateTime(submission.submittedAt)}</TableCell><TableCell className="text-sm font-medium">{formatGrade(submission.currentGrade, submission.gradeMax)}</TableCell><TableCell className="text-right"><Button type="button" size="sm" onClick={() => openGrade(submission)} disabled={!canManage}>Corrigir <Send className="ml-1 h-3.5 w-3.5" /></Button></TableCell></TableRow>; })}</TableBody></Table>{(submissionTotal > 25 || submissions.data?.data.hasMore || submissionPage > 1) && <div className="flex flex-col gap-3 border-t p-4 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between"><span>Página {submissionPage} de {totalSubmissionPages} · {submissionTotal} submissões</span><div className="flex gap-2"><Button type="button" variant="outline" size="sm" onClick={() => setSubmissionPage((value) => Math.max(1, value - 1))} disabled={submissionPage <= 1 || submissions.isFetching}><ChevronLeft className="mr-1 h-4 w-4" />Anterior</Button><Button type="button" variant="outline" size="sm" onClick={() => setSubmissionPage((value) => Math.min(totalSubmissionPages, value + 1))} disabled={submissionPage >= totalSubmissionPages || submissions.isFetching}>Próxima<ChevronRight className="ml-1 h-4 w-4" /></Button></div></div>}</>}</CardContent></Card>
    </section>}

    {courseId && !compact && <section className="space-y-4" aria-labelledby="evidence-title"><div className="flex items-end justify-between gap-3"><div><h2 id="evidence-title" className="flex items-center gap-2 text-lg font-semibold"><History className="h-5 w-5 text-primary" />Histórico de evidências</h2><p className="text-sm text-muted-foreground">Sinais observados automaticamente, preservados para contexto e auditoria.</p></div><Badge variant="outline">{evidence.data?.meta.total ?? evidence.data?.data.length ?? 0} registros</Badge></div><Card><CardContent>{evidence.isPending ? <p className="text-sm text-muted-foreground">Carregando histórico…</p> : evidence.isError ? <p className="text-sm text-destructive" role="alert">Não foi possível carregar o histórico de evidências.</p> : (evidence.data?.data.length ?? 0) === 0 ? <p className="text-sm text-muted-foreground">Nenhuma evidência registrada ainda.</p> : <ol className="relative ml-2 space-y-4 border-l pl-6">{evidence.data!.data.slice(0, 10).map((item) => <li key={item.id} className="relative"><span className="absolute -left-[1.82rem] mt-1 flex h-6 w-6 items-center justify-center rounded-full bg-primary/10 text-primary ring-4 ring-background"><History className="h-3 w-3" /></span><div><div className="flex flex-wrap items-center justify-between gap-2"><p className="text-sm font-medium">{item.title}</p><span className="text-xs text-muted-foreground">{formatDateTime(item.observedAt)}</span></div><p className="mt-1 text-sm text-muted-foreground">{item.details}</p></div></li>)}</ol>}</CardContent></Card></section>}

    <Dialog open={gradeOpen} onOpenChange={(open) => { if (!open) { setGradeOpen(false); setPrepareResult(null); } }}><DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl"><DialogHeader><DialogTitle>Correção manual</DialogTitle><DialogDescription>{activeSubmission?.fullName ?? activeSubmission?.userId} · {submissions.data?.data.assignmentName}</DialogDescription></DialogHeader>{activeSubmission && !prepareResult && <div className="grid gap-4">{submissionDetail.isPending ? <div className="rounded-md border bg-muted/20 p-3 text-sm text-muted-foreground">Carregando nota e feedback existentes…</div> : <div className="rounded-md border bg-muted/20 p-3 text-sm"><p className="font-medium">Contexto da submissão</p><p className="mt-1 text-muted-foreground">Enviada em {formatDateTime(activeSubmission.submittedAt)} · {activeSubmission.fileCount} arquivo(s) · {activeSubmission.hasOnlineText ? 'texto online disponível' : 'sem texto online'}</p><div className="mt-3 grid gap-2 sm:grid-cols-2"><div className="rounded border bg-background p-2"><p className="text-xs text-muted-foreground">Nota atual</p><p className="font-medium">{activeSubmission.currentGrade ?? 'Não lançada'}{activeSubmission.gradeMax ? ` / ${activeSubmission.gradeMax}` : ''}</p></div><div className="rounded border bg-background p-2"><p className="text-xs text-muted-foreground">Feedback existente</p><p className="whitespace-pre-wrap text-sm">{activeSubmission.currentFeedback || 'Nenhum feedback informado'}</p></div></div>{activeSubmission.files.length > 0 && <div className="mt-3 grid gap-1">{activeSubmission.files.map((file) => <a key={file.fileUrl} className="inline-flex items-center gap-1 text-primary hover:underline" href={file.fileUrl} target="_blank" rel="noreferrer"><FileText className="h-3.5 w-3.5" />{file.filename}</a>)}</div>}</div>}<label className="grid gap-1.5 text-sm font-medium">Nota<Input type="number" min="0" step="0.01" value={grade} onChange={(event) => setGrade(event.target.value)} placeholder="Ex.: 8,50" required /></label><label className="grid gap-1.5 text-sm font-medium">Feedback para o estudante<Textarea value={feedback} onChange={(event) => setFeedback(event.target.value)} placeholder="Feedback que será publicado no Moodle." /></label><label className="grid gap-1.5 text-sm font-medium">Justificativa interna<Textarea value={justification} onChange={(event) => setJustification(event.target.value)} placeholder="Explique o critério usado. Este registro fica na auditoria." required /></label>{submitPrepare.isError && <p className="text-sm text-destructive" role="alert">Não foi possível preparar a correção. Verifique a permissão de lançamento e os dados informados.</p>}<DialogFooter><Button type="button" variant="outline" onClick={() => setGradeOpen(false)}>Cancelar</Button><Button type="button" onClick={() => submitPrepare.mutate()} disabled={submitPrepare.isPending || !grade || Number(grade) < 0 || !justification.trim()}>{submitPrepare.isPending ? 'Preparando…' : 'Revisar e confirmar'}</Button></DialogFooter></div>}{prepareResult && <div className="grid gap-4"><div className="rounded-md border border-amber-300/60 bg-amber-50/60 p-4 dark:bg-amber-950/20"><div className="flex items-start gap-3"><ShieldCheck className="mt-0.5 h-5 w-5 text-amber-700" /><div><p className="font-medium">Prévia antes da escrita</p><p className="mt-1 text-sm text-muted-foreground">A nota atual é {prepareResult.preview.previousGrade ?? 'não informada'} e a nova nota será <strong>{prepareResult.preview.proposedGrade}</strong>. O Moodle será alterado somente após a confirmação exata.</p></div></div></div><div className="rounded-md border p-4"><p className="text-sm font-medium">Riscos e efeitos</p><ul className="mt-2 space-y-1 text-sm text-muted-foreground">{prepareResult.preview.risks.map((risk) => <li key={risk} className="flex gap-2"><AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />{risk}</li>)}</ul></div><label className="grid gap-1.5 text-sm font-medium">Digite exatamente: <code className="rounded bg-muted px-1.5 py-0.5 text-xs">{prepareResult.preview.confirmationText}</code><Input value={confirmation} onChange={(event) => setConfirmation(event.target.value)} placeholder="CONFIRMAR NOTA …" /></label>{confirmGrade.isError && <p className="text-sm text-destructive" role="alert">A confirmação não foi executada. A ação pode ter expirado ou o texto não está exato.</p>}<DialogFooter><Button type="button" variant="outline" onClick={() => setPrepareResult(null)}>Voltar</Button><Button type="button" onClick={() => confirmGrade.mutate()} disabled={confirmGrade.isPending || confirmation !== prepareResult.preview.confirmationText}>{confirmGrade.isPending ? 'Lançando no Moodle…' : 'Confirmar lançamento'}</Button></DialogFooter></div>}</DialogContent></Dialog>
    {confirmGrade.isSuccess && <p className="flex items-center gap-2 text-sm text-status-success" role="status"><CheckCircle2 className="h-4 w-4" />Nota lançada no Moodle e registrada na auditoria.</p>}
  </Container>;
}
