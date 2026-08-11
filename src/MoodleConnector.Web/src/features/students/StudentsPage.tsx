import { useQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import { ArrowRight, BookOpen, Search, Users } from 'lucide-react';
import { Avatar, AvatarFallback } from '../../components/ui/avatar';
import { Button } from '../../components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card';
import { Pagination } from '../../components/ui/pagination';
import { Skeleton } from '../../components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../../components/ui/table';
import { useConnectionScope, connectionDisplayName } from '../connections/useConnectionScope';
import { coursesGateway } from '../courses/courses-gateway';
import { EnrollmentBadge } from './components/EnrollmentBadge';
import { RiskBadge } from './components/RiskBadge';
import { studentsGateway } from './students-gateway';

function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]).join('').toUpperCase() || '?';
}

function formatDate(value?: string) {
  return value ? new Date(value).toLocaleDateString('pt-BR') : 'Nunca';
}

export function StudentsPage() {
  const [params, setParams] = useSearchParams();
  const { connectionRef, selectedConnection } = useConnectionScope();
  const courseId = params.get('courseId') || undefined;
  const page = Math.max(Number(params.get('page') || 1), 1);

  const courses = useQuery({
    queryKey: ['app', 'students', 'courses', connectionRef],
    queryFn: () => coursesGateway.list(connectionRef, 1, 100),
    enabled: Boolean(connectionRef),
    staleTime: 60_000,
  });
  const students = useQuery({
    queryKey: ['app', 'students', connectionRef, courseId, page],
    queryFn: () => studentsGateway.byCourse(connectionRef!, courseId!, page),
    enabled: Boolean(connectionRef && courseId),
  });

  const selectedCourse = courses.data?.data.find((course) => course.courseId === courseId);
  const totalPages = students.data?.meta.total !== undefined && students.data.meta.total !== null
    ? Math.max(1, Math.ceil(students.data.meta.total / students.data.meta.pageSize))
    : students.data?.meta.hasMore ? page + 1 : page;

  const updateCourse = (value: string) => {
    setParams((current) => {
      const next = new URLSearchParams(current);
      if (value) next.set('courseId', value);
      else next.delete('courseId');
      next.delete('page');
      return next;
    });
  };

  return (
    <main className="content-frame space-y-6">
      <header className="page-heading">
        <div>
          <p className="eyebrow">OPERACIONAL</p>
          <h1>Alunos</h1>
          <p>Consulte alunos, acessos, matrículas e risco de forma somente leitura.</p>
        </div>
        {students.data && <span className="freshness">Atualizado em {new Date(students.data.meta.generatedAt).toLocaleString('pt-BR')}</span>}
      </header>

      <Card>
        <CardHeader className="pb-4">
          <CardTitle className="flex items-center gap-2 text-lg"><BookOpen className="h-5 w-5 text-primary" /> Escopo da consulta</CardTitle>
          <CardDescription>Escolha um curso para consultar apenas os participantes desse contexto Moodle.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
            <label className="grid gap-1.5 text-sm font-medium" htmlFor="students-course">
              Curso
              <select
                id="students-course"
                aria-label="Curso"
                className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                value={courseId ?? ''}
                onChange={(event) => updateCourse(event.target.value)}
                disabled={!connectionRef || courses.isPending}
              >
                <option value="">{courses.isPending ? 'Carregando cursos…' : 'Selecione um curso'}</option>
                {courses.data?.data.map((course) => (
                  <option key={course.courseId} value={course.courseId}>
                    {course.displayName ?? course.fullName} ({course.courseId})
                  </option>
                ))}
              </select>
            </label>
            <div className="rounded-md bg-muted px-3 py-2 text-sm text-muted-foreground">
              Moodle: <span className="font-medium text-foreground">{connectionDisplayName(selectedConnection)}</span>
            </div>
          </div>
          {courses.isError && <p role="alert" className="text-sm text-destructive">Não foi possível carregar os cursos desta conexão.</p>}
          {!connectionRef && <p className="text-sm text-muted-foreground">Nenhuma conexão Moodle disponível para esta conta.</p>}
        </CardContent>
      </Card>

      {!courseId && (
        <Card className="border-dashed">
          <CardContent className="flex flex-col items-center gap-3 py-12 text-center">
            <div className="rounded-full bg-primary/10 p-3 text-primary"><Search className="h-6 w-6" /></div>
            <div><h2 className="font-semibold">Selecione um curso</h2><p className="mt-1 text-sm text-muted-foreground">A lista de alunos é sempre limitada ao curso escolhido.</p></div>
          </CardContent>
        </Card>
      )}

      {courseId && students.isPending && (
        <Card><CardContent className="space-y-3 py-6"><Skeleton className="h-10 w-full" /><Skeleton className="h-10 w-full" /><Skeleton className="h-10 w-full" /></CardContent></Card>
      )}
      {courseId && students.isError && <Card><CardContent className="py-8"><p role="alert" className="text-destructive">Não foi possível carregar os alunos deste curso.</p></CardContent></Card>}
      {courseId && students.isSuccess && students.data.data.length === 0 && (
        <Card className="border-dashed"><CardContent className="flex flex-col items-center gap-3 py-12 text-center"><Users className="h-8 w-8 text-muted-foreground" /><p className="text-sm text-muted-foreground">Nenhum aluno encontrado em {selectedCourse?.displayName ?? courseId}.</p></CardContent></Card>
      )}

      {courseId && students.isSuccess && students.data.data.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-lg">Participantes do curso</CardTitle>
            <CardDescription>{selectedCourse?.displayName ?? courseId} · {students.data.meta.returned} nesta página</CardDescription>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader><TableRow><TableHead>Aluno</TableHead><TableHead>Risco</TableHead><TableHead>Matrícula</TableHead><TableHead>Último acesso</TableHead><TableHead className="text-right">Ação</TableHead></TableRow></TableHeader>
              <TableBody>
                {students.data.data.map((student) => (
                  <TableRow key={`${student.connectionRef}:${student.studentId}`}>
                    <TableCell><div className="flex items-center gap-3"><Avatar className="h-9 w-9"><AvatarFallback>{initials(student.name)}</AvatarFallback></Avatar><div className="min-w-0"><p className="font-medium">{student.name}</p><p className="truncate text-xs text-muted-foreground">{student.email ?? 'Email não informado'}</p></div></div></TableCell>
                    <TableCell><RiskBadge level={student.risk} /></TableCell>
                    <TableCell><EnrollmentBadge status={student.suspended ? 'suspenso' : 'ativo'} /></TableCell>
                    <TableCell className="text-sm text-muted-foreground">{formatDate(student.lastCourseAccessAt)}</TableCell>
                    <TableCell className="text-right"><Button variant="ghost" size="sm" asChild><Link to={`/alunos/${encodeURIComponent(student.connectionRef)}/${encodeURIComponent(courseId)}/${encodeURIComponent(student.studentId)}`}>Abrir <ArrowRight className="h-4 w-4" /></Link></Button></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <Pagination page={page} totalPages={totalPages} onPageChange={(nextPage) => setParams((current) => { const next = new URLSearchParams(current); next.set('page', String(nextPage)); return next; })} />
          </CardContent>
        </Card>
      )}
    </main>
  );
}

