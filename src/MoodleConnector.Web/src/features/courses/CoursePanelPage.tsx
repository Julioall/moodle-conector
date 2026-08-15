import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { AlertTriangle, ArrowLeft, Calendar, CheckCircle2, ClipboardList, ExternalLink, Users } from 'lucide-react';
import { Link, useParams, useSearchParams } from 'react-router-dom';

import { Badge } from '../../components/ui/badge';
import { Button } from '../../components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card';
import { MoodleIcon } from '../../components/ui/MoodleIcon';
import { Progress } from '../../components/ui/progress';
import { Skeleton } from '../../components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../../components/ui/tabs';
import { coursesGateway } from './courses-gateway';
import { studentsGateway } from '../students/students-gateway';

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
  const normalized = risk?.trim().toLowerCase();
  return Boolean(normalized && normalized !== 'normal');
}

export function CoursePanelPage() {
  const { connectionRef = '', courseId = '' } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const activeTab = searchParams.get('tab') || 'overview';
  const [studentsPage, setStudentsPage] = useState(1);
  const [activitiesPage, setActivitiesPage] = useState(1);
  const enabled = Boolean(connectionRef && courseId);

  const course = useQuery({
    queryKey: ['app', 'course', connectionRef, courseId],
    queryFn: () => coursesGateway.get(connectionRef, courseId),
    enabled,
    staleTime: 60_000,
  });
  const activities = useQuery({
    queryKey: ['app', 'course-activities', connectionRef, courseId, activitiesPage],
    queryFn: () => coursesGateway.activities(connectionRef, courseId, activitiesPage, 20),
    enabled,
    staleTime: 30_000,
  });
  const students = useQuery({
    queryKey: ['app', 'course-students', connectionRef, courseId, studentsPage],
    queryFn: () => studentsGateway.byCourse(connectionRef, courseId, studentsPage, 25),
    enabled: enabled && activeTab === 'students',
    staleTime: 30_000,
  });

  const data = course.data?.data;
  const studentCount = students.data?.meta.total ?? students.data?.data.length;
  const activityCount = activities.data?.meta.total ?? activities.data?.data.length ?? 0;
  const atRiskCount = students.data?.data.filter((student) => isAtRisk(student.risk)).length ?? 0;
  const progress = data?.progress == null ? undefined : Math.max(0, Math.min(100, data.progress));
  const generatedAt = course.data?.meta.generatedAt;
  const studentTotalPages = Math.max(1, Math.ceil((studentCount ?? 0) / 25));
  const activityTotalPages = Math.max(1, Math.ceil(activityCount / 20));

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

      {data && (
        <>
          <header className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
            <div className="flex items-start gap-2">
              <Button variant="ghost" size="icon" asChild className="mt-0.5 h-8 w-8 shrink-0">
                <Link to={`/meus-cursos${connectionRef ? `?connectionRef=${encodeURIComponent(connectionRef)}` : ''}`} aria-label="Voltar para meus cursos">
                  <ArrowLeft className="h-4 w-4" />
                </Link>
              </Button>
              <div>
                <p className="eyebrow">CURSO · {connectionRef}</p>
                <h1 id="course-title" className="line-clamp-2 text-2xl font-bold tracking-tight">{data.displayName ?? data.fullName}</h1>
                <p className="mt-1 text-sm text-muted-foreground">{data.shortName ?? data.categoryName ?? 'Curso Moodle'}</p>
              </div>
            </div>
            <div className="flex flex-wrap items-center gap-3 md:justify-end">
              <div className="hidden text-right text-xs text-muted-foreground sm:block">
                <span>Última consulta:</span>
                <span className="ml-1 font-medium">{formatDateTime(generatedAt)}</span>
              </div>
              <Badge variant="outline">Somente leitura</Badge>
              {data.viewUrl && <Button variant="outline" size="sm" asChild><a href={data.viewUrl} target="_blank" rel="noreferrer">Abrir no Moodle <ExternalLink className="h-4 w-4" /></a></Button>}
            </div>
          </header>

          <div className="grid gap-4 md:grid-cols-4">
            <Card><CardContent className="p-4"><div className="flex items-center gap-3"><div className="rounded-lg bg-primary/10 p-2"><Users className="h-5 w-5 text-primary" /></div><div><p className="text-2xl font-bold">{studentCount ?? '—'}</p><p className="text-xs text-muted-foreground">{studentCount == null ? 'Abra a aba Alunos para consultar' : 'Alunos matriculados'}</p></div></div></CardContent></Card>
            <Card><CardContent className="p-4"><div className="flex items-center gap-3"><div className="rounded-lg bg-risk-risco/10 p-2"><AlertTriangle className="h-5 w-5 text-risk-risco" /></div><div><p className="text-2xl font-bold">{students.isPending ? '—' : students.data ? atRiskCount : '—'}</p><p className="text-xs text-muted-foreground">Alunos em risco na página</p></div></div></CardContent></Card>
            <Card><CardContent className="p-4"><div className="flex items-center gap-3"><div className="rounded-lg bg-primary/10 p-2"><ClipboardList className="h-5 w-5 text-primary" /></div><div><p className="text-2xl font-bold">{activityCount}</p><p className="text-xs text-muted-foreground">Atividades disponíveis</p></div></div></CardContent></Card>
            <Card><CardContent className="p-4"><div className="flex items-center gap-3"><div className="rounded-lg bg-green-500/10 p-2"><CheckCircle2 className="h-5 w-5 text-green-500" /></div><div><p className="text-2xl font-bold">{progress == null ? '—' : `${Math.round(progress)}%`}</p><p className="text-xs text-muted-foreground">Progresso retornado</p></div></div></CardContent></Card>
          </div>

          <Tabs value={activeTab} onValueChange={handleTabChange} className="space-y-4">
            <TabsList aria-label="Seções do curso">
              <TabsTrigger value="overview">Visão geral</TabsTrigger>
              <TabsTrigger value="students">Alunos{studentCount == null ? '' : ` (${studentCount})`}</TabsTrigger>
              <TabsTrigger value="activities">Atividades ({activityCount})</TabsTrigger>
            </TabsList>

            <TabsContent value="overview" className="space-y-4">
              <Card><CardHeader><CardTitle className="text-base">Informações do curso</CardTitle><CardDescription>Dados consultados na conexão Moodle selecionada.</CardDescription></CardHeader><CardContent className="grid gap-4 text-sm sm:grid-cols-2 lg:grid-cols-4"><div className="flex items-center gap-2"><Calendar className="h-4 w-4 text-muted-foreground" /><span className="text-muted-foreground">Início</span><strong>{formatDate(data.startDate)}</strong></div><div className="flex items-center gap-2"><Calendar className="h-4 w-4 text-muted-foreground" /><span className="text-muted-foreground">Encerramento</span><strong>{formatDate(data.endDate)}</strong></div><div className="flex items-center gap-2"><MoodleIcon className="h-4 w-4" /><span className="text-muted-foreground">ID Moodle</span><strong>{courseId}</strong></div><div><span className="text-muted-foreground">Último acesso</span><p className="mt-1 font-medium">{formatDateTime(data.lastAccessAt)}</p></div></CardContent></Card>
              <Card><CardHeader><CardTitle className="text-base">Acompanhamento do curso</CardTitle><CardDescription>Percentual informado pelo Moodle para a conexão atual.</CardDescription></CardHeader><CardContent>{progress == null ? <p className="text-sm text-muted-foreground">O Moodle não retornou um percentual de progresso.</p> : <div className="space-y-2"><div className="flex items-center justify-between text-sm"><span>Progresso</span><span className="font-medium">{Math.round(progress)}%</span></div><Progress value={progress} className="h-2" /><p className="text-xs text-muted-foreground">O indicador é informativo e não altera dados no Moodle.</p></div>}</CardContent></Card>
            </TabsContent>

            <TabsContent value="students"><Card><CardHeader><CardTitle className="flex items-center gap-2"><Users className="h-5 w-5 text-primary" />Alunos do curso</CardTitle><CardDescription>Participantes limitados à página atual e à conexão selecionada.</CardDescription></CardHeader><CardContent>
              {students.isPending && <div className="space-y-3"><Skeleton className="h-10 w-full" /><Skeleton className="h-10 w-full" /></div>}
              {students.isError && <p role="alert" className="text-destructive">Não foi possível carregar os alunos.</p>}
              {students.isSuccess && <p className="mb-3 text-xs text-muted-foreground">Consulta paginada e limitada ao curso atual.</p>}
              {students.isSuccess && students.data.data.length === 0 && <p className="text-sm text-muted-foreground">Nenhum aluno encontrado.</p>}
              {students.isSuccess && students.data.data.length > 0 && <div className="divide-y">{students.data.data.map((student) => <div key={`${student.connectionRef}:${student.studentId}`} className="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-0"><div><p className="font-medium">{student.name}</p><p className="text-xs text-muted-foreground">{student.email ?? 'E-mail não informado'}{student.lastCourseAccessAt ? ` · último acesso ${formatDateTime(student.lastCourseAccessAt)}` : ''}</p></div><div className="flex items-center gap-2"><Badge variant={isAtRisk(student.risk) ? 'destructive' : 'secondary'}>{student.risk || 'normal'}</Badge><Button variant="outline" size="sm" asChild><Link to={`/cursos/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}/alunos/${encodeURIComponent(student.studentId)}`}>Abrir perfil</Link></Button></div></div>)}</div>}
              {studentTotalPages > 1 && <div className="mt-4 flex items-center justify-between gap-3 border-t pt-4 text-xs text-muted-foreground"><span>Página {studentsPage} de {studentTotalPages}</span><div className="flex gap-2"><Button variant="outline" size="sm" onClick={() => setStudentsPage((page) => Math.max(1, page - 1))} disabled={studentsPage <= 1}>Anterior</Button><Button variant="outline" size="sm" onClick={() => setStudentsPage((page) => Math.min(studentTotalPages, page + 1))} disabled={studentsPage >= studentTotalPages}>Próxima</Button></div></div>}
            </CardContent></Card></TabsContent>

            <TabsContent value="activities"><Card><CardHeader><CardTitle>Atividades</CardTitle><CardDescription>Datas, tipos e links informativos retornados pelo Moodle.</CardDescription></CardHeader><CardContent>
              {activities.isPending && <div className="space-y-3"><Skeleton className="h-12 w-full" /><Skeleton className="h-12 w-full" /></div>}
              {activities.isError && <p role="alert" className="text-destructive">Não foi possível carregar as atividades.</p>}
              {activities.isSuccess && activities.data.data.length === 0 && <p className="text-sm text-muted-foreground">Nenhuma atividade disponível.</p>}
              {activities.isSuccess && activities.data.data.length > 0 && <div className="divide-y">{activities.data.data.map((activity) => <article className="flex flex-wrap items-center justify-between gap-3 py-4 first:pt-0" key={`${activity.connectionRef}:${activity.courseId}:${activity.activityId}`}><div><p className="font-medium">{activity.name}</p><p className="text-xs text-muted-foreground">{activity.activityType}{activity.dueAt ? ` · prazo ${formatDate(activity.dueAt)}` : ' · sem prazo'}</p></div>{activity.url && <a className="text-sm text-primary hover:underline" href={activity.url} target="_blank" rel="noreferrer">Ver no Moodle</a>}</article>)}</div>}
              {activityTotalPages > 1 && <div className="mt-4 flex items-center justify-between gap-3 border-t pt-4 text-xs text-muted-foreground"><span>Página {activitiesPage} de {activityTotalPages}</span><div className="flex gap-2"><Button variant="outline" size="sm" onClick={() => setActivitiesPage((page) => Math.max(1, page - 1))} disabled={activitiesPage <= 1}>Anterior</Button><Button variant="outline" size="sm" onClick={() => setActivitiesPage((page) => Math.min(activityTotalPages, page + 1))} disabled={activitiesPage >= activityTotalPages}>Próxima</Button></div></div>}
            </CardContent></Card></TabsContent>
          </Tabs>
        </>
      )}
    </main>
  );
}
