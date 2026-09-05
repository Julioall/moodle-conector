import { useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, ExternalLink, MessageCircle, RefreshCw, Search, Users } from 'lucide-react';
import { Link, useParams } from 'react-router-dom';

import { Badge } from '../../components/ui/badge';
import { Button } from '../../components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card';
import { Input } from '../../components/ui/input';
import { Skeleton } from '../../components/ui/skeleton';
import { studentsGateway, type Student } from '../students/students-gateway';
import { CourseFollowupDialog } from './CourseFollowupDialog';
import { coursesGateway } from './courses-gateway';

function formatDateTime(value?: string) {
  if (!value) return 'Nunca acessou';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Não informado' : date.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

export function CoursePanelPage() {
  const { connectionRef = '', courseId = '' } = useParams();
  const [studentsPage, setStudentsPage] = useState(1);
  const [studentSearch, setStudentSearch] = useState('');
  const [followupOpen, setFollowupOpen] = useState(false);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const enabled = Boolean(connectionRef && courseId);
  const queryClient = useQueryClient();

  const course = useQuery({ queryKey: ['app', 'course', connectionRef, courseId], queryFn: () => coursesGateway.get(connectionRef, courseId), enabled, staleTime: 60_000 });
  const students = useQuery({
    queryKey: ['app', 'course-students', connectionRef, courseId, studentsPage],
    queryFn: () => studentsGateway.byCourse(connectionRef, courseId, studentsPage, 25),
    enabled,
    staleTime: 30_000,
    refetchInterval: (query) => query.state.data?.meta.source === 'background' || query.state.data?.meta.refreshQueued || query.state.data?.meta.complete === false ? 2_000 : false,
  });

  const data = course.data?.data;
  const studentTotalPages = Math.max(1, students.data?.meta.total ? Math.ceil(students.data.meta.total / 25) : students.data?.meta.hasMore ? studentsPage + 1 : studentsPage);
  const visibleStudents = useMemo(() => {
    const query = studentSearch.trim().toLocaleLowerCase();
    return (students.data?.data ?? []).filter((student) => !query || `${student.name} ${student.email ?? ''}`.toLocaleLowerCase().includes(query));
  }, [studentSearch, students.data?.data]);

  const refreshCourseData = async () => {
    if (!enabled || isRefreshing) return;
    setIsRefreshing(true);
    try {
      await queryClient.fetchQuery({
        queryKey: ['app', 'course', connectionRef, courseId],
        queryFn: () => coursesGateway.get(connectionRef, courseId, true),
        staleTime: 0,
      });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['app', 'course-students', connectionRef, courseId] }),
      ]);
    } finally {
      setIsRefreshing(false);
    }
  };

  return (
    <main className="space-y-6 animate-fade-in" aria-labelledby="course-title">
      {course.isPending && <Card><CardContent className="space-y-4 py-8"><Skeleton className="h-8 w-2/3" /><Skeleton className="h-4 w-1/3" /></CardContent></Card>}
      {course.isError && <Card><CardContent className="py-8"><p role="alert" className="text-destructive">Não foi possível carregar o curso.</p></CardContent></Card>}
      {!course.isPending && !course.isError && !data && <Card><CardContent className="py-8"><p>Curso não encontrado nesta conexão.</p></CardContent></Card>}

      {data && <>
        <header className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
          <div className="flex items-start gap-2"><Button variant="ghost" size="icon" asChild className="mt-0.5 h-8 w-8 shrink-0"><Link to={`/meus-cursos${connectionRef ? `?connectionRef=${encodeURIComponent(connectionRef)}` : ''}`} aria-label="Voltar para meus cursos"><ArrowLeft className="h-4 w-4" /></Link></Button><div><p className="eyebrow">CURSO · {connectionRef}</p><h1 id="course-title" className="line-clamp-2 text-2xl font-bold tracking-tight">{data.displayName ?? data.fullName}</h1><p className="mt-1 text-sm text-muted-foreground">Últimos acessos e notas da turma.</p></div></div>
          <div className="flex flex-wrap items-center gap-2 md:justify-end"><Button variant="outline" size="sm" onClick={() => void refreshCourseData()} disabled={isRefreshing}><RefreshCw className={`h-4 w-4 ${isRefreshing ? 'animate-spin' : ''}`} />Atualizar</Button><Button variant="outline" size="sm" onClick={() => setFollowupOpen(true)}><MessageCircle className="h-4 w-4" />Registrar acompanhamento</Button>{data.viewUrl && <Button variant="outline" size="sm" asChild><a href={data.viewUrl} target="_blank" rel="noreferrer">Abrir no Moodle <ExternalLink className="h-4 w-4" /></a></Button>}</div>
        </header>

        <section className="space-y-4"><Card><CardHeader><div className="flex flex-wrap items-start justify-between gap-3"><div><CardTitle className="flex items-center gap-2 text-base"><Users className="h-4 w-4" />Alunos do curso</CardTitle><CardDescription>Último acesso por aluno.</CardDescription></div>{students.data?.meta.total != null && <Badge variant="outline">{students.data.meta.total} alunos</Badge>}</div></CardHeader><CardContent><label className="relative mb-4 block"><span className="sr-only">Buscar aluno</span><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" /><Input className="pl-9" value={studentSearch} onChange={(event) => setStudentSearch(event.target.value)} placeholder="Buscar aluno ou e-mail" /></label>{students.isPending ? <div className="space-y-3"><Skeleton className="h-12 w-full" /><Skeleton className="h-12 w-full" /></div> : students.isError ? <p role="alert" className="text-destructive">Não foi possível carregar os alunos.</p> : visibleStudents.length === 0 ? <p className="py-8 text-center text-sm text-muted-foreground">Nenhum aluno encontrado.</p> : <div className="divide-y">{visibleStudents.map((student: Student) => <div key={`${student.connectionRef}:${student.studentId}`} className="grid gap-3 py-3 md:grid-cols-[minmax(0,1fr)_220px_auto] md:items-center"><div><p className="font-medium">{student.name}</p><p className="text-xs text-muted-foreground">{student.email ?? 'E-mail não informado'}</p></div><div><p className="text-xs text-muted-foreground">Último acesso</p><p className="text-sm">{formatDateTime(student.lastCourseAccessAt)}</p></div><Button variant="outline" size="sm" asChild><Link to={`/cursos/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}/alunos/${encodeURIComponent(student.studentId)}`}>Ver notas</Link></Button></div>)}</div>}{studentTotalPages > 1 && <div className="mt-4 flex items-center justify-between gap-3 border-t pt-4 text-xs text-muted-foreground"><span>Página {studentsPage} de {studentTotalPages}</span><div className="flex gap-2"><Button variant="outline" size="sm" onClick={() => setStudentsPage((page) => Math.max(1, page - 1))} disabled={studentsPage <= 1}>Anterior</Button><Button variant="outline" size="sm" onClick={() => setStudentsPage((page) => Math.min(studentTotalPages, page + 1))} disabled={studentsPage >= studentTotalPages}>Próxima</Button></div></div>}</CardContent></Card></section>
        <CourseFollowupDialog open={followupOpen} onOpenChange={setFollowupOpen} connectionRef={connectionRef} courseId={courseId} students={students.data?.data ?? []} />
      </>}
    </main>
  );
}
