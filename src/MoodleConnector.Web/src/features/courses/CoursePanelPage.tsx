import { useQuery } from '@tanstack/react-query';
import { ArrowLeft, Calendar, ExternalLink, Users } from 'lucide-react';
import { Link, useParams } from 'react-router-dom';
import { Badge } from '../../components/ui/badge';
import { Button } from '../../components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card';
import { Skeleton } from '../../components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../../components/ui/tabs';
import { coursesGateway } from './courses-gateway';
import { studentsGateway } from '../students/students-gateway';

function formatDate(value?: string) {
  if (!value) return 'NÃ£o informado';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'NÃ£o informado' : date.toLocaleDateString('pt-BR');
}

export function CoursePanelPage() {
  const { connectionRef = '', courseId = '' } = useParams();
  const enabled = Boolean(connectionRef && courseId);
  const course = useQuery({
    queryKey: ['app', 'course', connectionRef, courseId],
    queryFn: () => coursesGateway.get(connectionRef, courseId),
    enabled,
  });
  const activities = useQuery({
    queryKey: ['app', 'course-activities', connectionRef, courseId],
    queryFn: () => coursesGateway.activities(connectionRef, courseId),
    enabled,
  });
  const students = useQuery({
    queryKey: ['app', 'course-students', connectionRef, courseId],
    queryFn: () => studentsGateway.byCourse(connectionRef, courseId),
    enabled,
  });

  const data = course.data?.data;

  return (
    <main className="space-y-6" aria-labelledby="course-title">
      <Button variant="ghost" size="sm" asChild>
        <Link to={`/meus-cursos${connectionRef ? `?connectionRef=${encodeURIComponent(connectionRef)}` : ''}`}>
          <ArrowLeft className="h-4 w-4" /> Voltar para meus cursos
        </Link>
      </Button>

      {course.isPending && <Card><CardContent className="space-y-4 py-8"><Skeleton className="h-8 w-2/3" /><Skeleton className="h-4 w-1/3" /></CardContent></Card>}
      {course.isError && <Card><CardContent className="py-8"><p role="alert" className="text-destructive">NÃ£o foi possÃ­vel carregar o curso.</p></CardContent></Card>}
      {!course.isPending && !course.isError && !data && <Card><CardContent className="py-8"><p>Curso nÃ£o encontrado nesta conexÃ£o.</p></CardContent></Card>}

      {data && (
        <>
          <header className="flex flex-col gap-4 rounded-lg border bg-card p-6 shadow-sm md:flex-row md:items-start md:justify-between">
            <div>
              <p className="mb-1 text-[10px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">Curso Â· {connectionRef}</p>
              <h1 id="course-title" className="text-2xl font-semibold tracking-tight">{data.displayName ?? data.fullName}</h1>
              <p className="mt-1 text-sm text-muted-foreground">{data.shortName ?? data.categoryName ?? 'Curso Moodle'}</p>
            </div>
            <div className="flex items-center gap-2">
              <Badge variant="outline">Somente leitura</Badge>
              {data.viewUrl && <Button variant="outline" size="sm" asChild><a href={data.viewUrl} target="_blank" rel="noreferrer">Abrir no Moodle <ExternalLink className="h-4 w-4" /></a></Button>}
            </div>
          </header>

          <Tabs defaultValue="overview" className="space-y-4">
            <TabsList aria-label="SeÃ§Ãµes do curso">
              <TabsTrigger value="overview">VisÃ£o geral</TabsTrigger>
              <TabsTrigger value="students">Alunos</TabsTrigger>
              <TabsTrigger value="activities">Atividades</TabsTrigger>
            </TabsList>

            <TabsContent value="overview" className="space-y-4">
              <div className="grid gap-4 sm:grid-cols-3">
                <Card><CardHeader className="pb-2"><CardDescription>Progresso</CardDescription><CardTitle className="text-2xl">{data.progress != null ? `${Math.round(data.progress)}%` : 'â€”'}</CardTitle></CardHeader><CardContent className="text-xs text-muted-foreground">Acompanhamento retornado pelo Moodle</CardContent></Card>
                <Card><CardHeader className="pb-2"><CardDescription>InÃ­cio</CardDescription><CardTitle className="text-base">{formatDate(data.startDate)}</CardTitle></CardHeader><CardContent><Calendar className="h-4 w-4 text-muted-foreground" /></CardContent></Card>
                <Card><CardHeader className="pb-2"><CardDescription>Encerramento</CardDescription><CardTitle className="text-base">{formatDate(data.endDate)}</CardTitle></CardHeader><CardContent><Calendar className="h-4 w-4 text-muted-foreground" /></CardContent></Card>
              </div>
              <Card><CardHeader><CardTitle>Contexto do curso</CardTitle><CardDescription>Dados consultados na conexÃ£o Moodle selecionada.</CardDescription></CardHeader><CardContent className="grid gap-3 text-sm sm:grid-cols-2"><div><span className="text-muted-foreground">Identidade composta</span><p className="font-mono text-xs">{connectionRef}:{courseId}</p></div><div><span className="text-muted-foreground">Ãšltimo acesso</span><p>{formatDate(data.lastAccessAt)}</p></div></CardContent></Card>
            </TabsContent>

            <TabsContent value="students">
              <Card><CardHeader><CardTitle className="flex items-center gap-2"><Users className="h-5 w-5 text-primary" /> Alunos do curso</CardTitle><CardDescription>Participantes limitados ao curso e Ã  conexÃ£o atuais.</CardDescription></CardHeader><CardContent>
                {students.isPending && <div className="space-y-3"><Skeleton className="h-10 w-full" /><Skeleton className="h-10 w-full" /></div>}
                {students.isError && <p role="alert" className="text-destructive">NÃ£o foi possÃ­vel carregar os alunos.</p>}
                {students.isSuccess && students.data.data.length === 0 && <p className="text-sm text-muted-foreground">Nenhum aluno encontrado.</p>}
                {students.isSuccess && students.data.data.length > 0 && <div className="divide-y">{students.data.data.map((student) => <div key={`${student.connectionRef}:${student.studentId}`} className="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-0"><div><p className="font-medium">{student.name}</p><p className="text-xs text-muted-foreground">{student.email ?? 'Email nÃ£o informado'}</p></div><Button variant="outline" size="sm" asChild><Link to={`/alunos/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}/${encodeURIComponent(student.studentId)}`}>Abrir perfil</Link></Button></div>)}</div>}
              </CardContent></Card>
            </TabsContent>

            <TabsContent value="activities">
              <Card><CardHeader><CardTitle>Atividades</CardTitle><CardDescription>Datas, tipos e links informativos retornados pelo Moodle.</CardDescription></CardHeader><CardContent>
                {activities.isPending && <div className="space-y-3"><Skeleton className="h-12 w-full" /><Skeleton className="h-12 w-full" /></div>}
                {activities.isError && <p role="alert" className="text-destructive">NÃ£o foi possÃ­vel carregar as atividades.</p>}
                {activities.isSuccess && activities.data.data.length === 0 && <p className="text-sm text-muted-foreground">Nenhuma atividade disponÃ­vel.</p>}
                {activities.isSuccess && activities.data.data.length > 0 && <div className="divide-y">{activities.data.data.map((activity) => <article className="flex flex-wrap items-center justify-between gap-3 py-4 first:pt-0" key={`${activity.connectionRef}:${activity.courseId}:${activity.activityId}`}><div><p className="font-medium">{activity.name}</p><p className="text-xs text-muted-foreground">{activity.activityType}{activity.dueAt ? ` Â· prazo ${formatDate(activity.dueAt)}` : ' Â· sem prazo'}</p></div>{activity.url && <a className="text-sm text-primary hover:underline" href={activity.url} target="_blank" rel="noreferrer">Ver no Moodle</a>}</article>)}</div>}
              </CardContent></Card>
            </TabsContent>
          </Tabs>
        </>
      )}
    </main>
  );
}

