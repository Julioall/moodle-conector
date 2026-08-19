import { useEffect, useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowRight, BookOpen, Check, Search, Sparkles } from 'lucide-react';
import { useNavigate } from 'react-router-dom';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { useConnectionScope } from '../connections/useConnectionScope';
import { coursesGateway, type Course } from './courses-gateway';
import { filterCoursesByLifecycle, matchesCourseSearch, normalizeCourseEndDatesBySequence } from './course-status';
import { useIgnoredCourses } from './course-visibility';

function courseTitle(course: Course) {
  return course.displayName ?? course.fullName;
}

export function CourseSelectionPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { connectionRef, selectedConnection } = useConnectionScope();
  const { ignoredCourseIds, isLoading: preferencesLoading, isSaving, replaceIgnoredCourses } = useIgnoredCourses(connectionRef);
  const [search, setSearch] = useState('');
  const [selectedCourseIds, setSelectedCourseIds] = useState<Set<string>>(new Set());
  const [initializedFor, setInitializedFor] = useState<string>();
  const [savingError, setSavingError] = useState('');

  const query = useQuery({
    queryKey: ['app', 'course-selection', connectionRef],
    queryFn: () => coursesGateway.listAll(connectionRef, 100),
    enabled: Boolean(connectionRef),
    staleTime: 60_000,
  });
  const courses = useMemo(() => filterCoursesByLifecycle(normalizeCourseEndDatesBySequence(query.data?.data ?? []), 'in_progress'), [query.data?.data]);
  const filteredCourses = useMemo(() => courses.filter((course) => matchesCourseSearch(course, search)), [courses, search]);

  useEffect(() => {
    if (!connectionRef || !query.isSuccess || preferencesLoading || initializedFor === connectionRef) return;
    setSelectedCourseIds(new Set(courses.filter((course) => !ignoredCourseIds.has(course.courseId)).map((course) => course.courseId)));
    setInitializedFor(connectionRef);
  }, [connectionRef, courses, ignoredCourseIds, initializedFor, preferencesLoading, query.isSuccess]);

  const toggleCourse = (courseId: string) => setSelectedCourseIds((current) => {
    const next = new Set(current);
    if (next.has(courseId)) next.delete(courseId); else next.add(courseId);
    return next;
  });

  const toggleFiltered = () => setSelectedCourseIds((current) => {
    const next = new Set(current);
    const shouldSelect = filteredCourses.some((course) => !next.has(course.courseId));
    filteredCourses.forEach((course) => shouldSelect ? next.add(course.courseId) : next.delete(course.courseId));
    return next;
  });

  const completeSelection = async () => {
    if (!connectionRef || isSaving) return;
    setSavingError('');
    try {
      await replaceIgnoredCourses(courses.map((course) => course.courseId), selectedCourseIds);
      await queryClient.invalidateQueries({ queryKey: ['app', 'courses'] });
      await queryClient.invalidateQueries({ queryKey: ['app', 'dashboard'] });
      navigate(connectionRef ? `/?connectionRef=${encodeURIComponent(connectionRef)}` : '/', { replace: true });
    } catch (error) {
      setSavingError(error instanceof Error ? error.message : 'Não foi possível salvar a seleção de cursos.');
    }
  };

  const selectedCount = selectedCourseIds.size;
  const allFilteredSelected = filteredCourses.length > 0 && filteredCourses.every((course) => selectedCourseIds.has(course.courseId));

  return (
    <main className="mx-auto w-full max-w-6xl space-y-6 animate-fade-in" aria-labelledby="course-selection-title">
      <header className="page-heading">
        <div>
          <p className="eyebrow">PRÉ-CONFIGURAÇÃO</p>
          <h1 id="course-selection-title">Escolha os cursos que você acompanha</h1>
          <p>Antes de montar o seu painel, selecione somente as turmas que fazem parte da sua rotina.</p>
        </div>
      </header>

      <Card className="border-primary/25 bg-primary/[0.03]">
        <CardContent className="flex flex-col gap-4 p-5 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary"><Sparkles className="h-5 w-5" /></div>
            <div>
              <p className="font-medium">Uma configuração rápida para deixar o painel mais leve</p>
              <p className="mt-1 text-sm text-muted-foreground">{selectedConnection?.alias ? `Conexão: ${selectedConnection.alias}. ` : ''}Os cursos desmarcados ficarão fora de Meus Cursos e poderão ser adicionados novamente em Escolas.</p>
            </div>
          </div>
          <Badge variant="secondary" className="w-fit shrink-0">{selectedCount} selecionado{selectedCount === 1 ? '' : 's'}</Badge>
        </CardContent>
      </Card>

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative w-full sm:w-80"><Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" /><Input type="search" placeholder="Buscar curso ou turma..." value={search} onChange={(event) => setSearch(event.target.value)} className="pl-9" aria-label="Buscar cursos para acompanhar" /></div>
        <Button type="button" variant="outline" onClick={toggleFiltered} disabled={filteredCourses.length === 0}>{allFilteredSelected ? 'Desmarcar resultados' : 'Selecionar resultados'}</Button>
      </div>

      {query.isPending || preferencesLoading ? <div className="grid gap-4 md:grid-cols-2"><Skeleton className="h-44 rounded-lg" /><Skeleton className="h-44 rounded-lg" /></div> : null}
      {query.isError && <Card><CardContent className="p-6 text-sm text-destructive" role="alert">Não foi possível carregar os cursos para a configuração inicial.</CardContent></Card>}
      {query.isSuccess && courses.length === 0 && <Card className="border-dashed"><CardContent className="flex flex-col items-center gap-3 p-12 text-center"><BookOpen className="h-10 w-10 text-muted-foreground/40" /><h2 className="font-medium">Nenhum curso em andamento encontrado</h2><p className="text-sm text-muted-foreground">A conexão Moodle foi criada, mas não há turmas atuais para configurar.</p><Button type="button" onClick={() => navigate('/')}>Ir para o painel</Button></CardContent></Card>}
      {query.isSuccess && filteredCourses.length > 0 && <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-3" aria-label="Cursos disponíveis para acompanhamento">{filteredCourses.map((course) => <Card key={`${course.connectionRef}:${course.courseId}`} className={`relative h-full transition-colors ${selectedCourseIds.has(course.courseId) ? 'border-primary/50 bg-primary/[0.03]' : 'opacity-75'}`}><button type="button" className="flex h-full w-full items-start gap-3 p-4 text-left" onClick={() => toggleCourse(course.courseId)} aria-pressed={selectedCourseIds.has(course.courseId)}><span className={`mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded border ${selectedCourseIds.has(course.courseId) ? 'border-primary bg-primary text-primary-foreground' : 'border-muted-foreground/40'}`}>{selectedCourseIds.has(course.courseId) && <Check className="h-3.5 w-3.5" />}</span><span className="min-w-0"><span className="line-clamp-2 font-semibold">{courseTitle(course)}</span><span className="mt-1 block truncate text-xs text-muted-foreground">{course.categoryName ?? course.shortName ?? 'Curso Moodle'}</span></span></button></Card>)}</section>}

      {savingError && <p className="text-sm text-destructive" role="alert">{savingError}</p>}
      <div className="sticky bottom-4 flex flex-col gap-3 rounded-lg border bg-card/95 p-4 shadow-lg backdrop-blur sm:flex-row sm:items-center sm:justify-between"><div className="text-sm text-muted-foreground">Você poderá alterar essa seleção depois em <span className="font-medium text-foreground">Escolas</span>.</div><Button type="button" onClick={() => void completeSelection()} disabled={query.isPending || preferencesLoading || isSaving}>{isSaving ? 'Salvando seleção…' : 'Concluir e abrir meu painel'}<ArrowRight className="ml-2 h-4 w-4" /></Button></div>
    </main>
  );
}
