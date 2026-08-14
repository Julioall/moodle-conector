import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import { ArrowRight, BookOpen, RefreshCw, Search, Users } from 'lucide-react';
import { Avatar, AvatarFallback } from '../../components/ui/avatar';
import { Button } from '../../components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card';
import { Input } from '../../components/ui/input';
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
    queryKey: ['app', 'courses', connectionRef],
    queryFn: () => coursesGateway.list(connectionRef, 1, 100),
    enabled: Boolean(connectionRef),
    staleTime: 60_000,
  });
  const students = useQuery({
    queryKey: ['app', 'students', connectionRef, courseId],
    queryFn: () => studentsGateway.byCourse(connectionRef!, courseId!, 1, 100),
    enabled: Boolean(connectionRef && courseId),
    staleTime: 30_000,
  });
  const search = params.get('search') ?? '';
  const risk = params.get('risk') ?? 'all';
  const status = params.get('status') ?? 'all';

  const selectedCourse = courses.data?.data.find((course) => course.courseId === courseId);
  const filteredStudents = useMemo(() => {
    const normalizedSearch = search.trim().toLocaleLowerCase('pt-BR');
    return (students.data?.data ?? []).filter((student) => {
      const matchesSearch = !normalizedSearch || `${student.name} ${student.email ?? ''}`.toLocaleLowerCase('pt-BR').includes(normalizedSearch);
      const matchesRisk = risk === 'all' || student.risk === risk || (risk === 'critico' && student.risk === 'critical') || (risk === 'risco' && student.risk === 'risk') || (risk === 'atencao' && student.risk === 'attention');
      const studentStatus = student.suspended ? 'suspenso' : 'ativo';
      const matchesStatus = status === 'all' || status === studentStatus;
      return matchesSearch && matchesRisk && matchesStatus;
    });
  }, [risk, search, status, students.data?.data]);
  const pageSize = 30;
  const totalPages = Math.max(1, Math.ceil(filteredStudents.length / pageSize));
  const paginatedStudents = filteredStudents.slice((page - 1) * pageSize, page * pageSize);

  const updateCourse = (value: string) => {
    setParams((current) => {
      const next = new URLSearchParams(current);
      if (value) next.set('courseId', value);
      else next.delete('courseId');
      next.delete('page');
      return next;
    });
  };
  const updateFilter = (key: string, value: string) => {
    setParams((current) => {
      const next = new URLSearchParams(current);
      if (value && value !== 'all') next.set(key, value);
      else next.delete(key);
      next.delete('page');
      return next;
    }, { replace: true });
  };

  return (
    <main className="space-y-6 animate-fade-in">
      <header className="page-heading">
        <div>
          <p className="eyebrow">OPERACIONAL</p>
          <h1>Alunos</h1>
          <p>Consulte alunos, acessos, matrículas e risco de forma somente leitura.</p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          {students.data && <span className="freshness">Atualizado em {new Date(students.data.meta.generatedAt).toLocaleString('pt-BR')}</span>}
          <Button type="button" variant="outline" size="sm" onClick={() => { void courses.refetch(); if (courseId) void students.refetch(); }} disabled={!connectionRef || courses.isFetching || students.isFetching}>
            <RefreshCw className={(courses.isFetching || students.isFetching) ? 'animate-spin' : ''} /> Atualizar
          </Button>
        </div>
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
      {courseId && students.isSuccess && <div className="flex flex-wrap items-center gap-2"><div className="relative min-w-56 flex-1"><Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" /><Input value={search} onChange={(event) => updateFilter('search', event.target.value)} placeholder="Buscar aluno ou e-mail…" className="pl-9" aria-label="Buscar aluno" /></div><select value={risk} onChange={(event) => updateFilter('risk', event.target.value)} className="flex h-10 w-36 rounded-md border border-input bg-background px-3 py-2 text-sm" aria-label="Filtrar por risco"><option value="all">Todos os riscos</option><option value="critico">Crítico</option><option value="risco">Risco</option><option value="atencao">Atenção</option><option value="normal">Normal</option></select><select value={status} onChange={(event) => updateFilter('status', event.target.value)} className="flex h-10 w-36 rounded-md border border-input bg-background px-3 py-2 text-sm" aria-label="Filtrar por status"><option value="all">Todos os status</option><option value="ativo">Ativo</option><option value="suspenso">Suspenso</option></select></div>}
      {courseId && students.isSuccess && filteredStudents.length === 0 && (
        <Card className="border-dashed"><CardContent className="flex flex-col items-center gap-3 py-12 text-center"><Users className="h-8 w-8 text-muted-foreground" /><p className="text-sm text-muted-foreground">Nenhum aluno encontrado em {selectedCourse?.displayName ?? courseId}.</p></CardContent></Card>
      )}

      {courseId && students.isSuccess && filteredStudents.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-lg">Participantes do curso</CardTitle>
            <CardDescription>{selectedCourse?.displayName ?? courseId} · {filteredStudents.length} encontrados nesta consulta</CardDescription>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader><TableRow><TableHead>Aluno</TableHead><TableHead>Risco</TableHead><TableHead>Matrícula</TableHead><TableHead>Último acesso</TableHead><TableHead className="text-right">Ação</TableHead></TableRow></TableHeader>
              <TableBody>
                {paginatedStudents.map((student) => (
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

