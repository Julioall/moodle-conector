import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { BookOpen, Building2, ChevronDown, EyeOff, FileSpreadsheet, Plus, Search } from 'lucide-react';

import { Card, CardContent } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
import { useEditMode } from '@/components/layout/edit-mode-context';
import { useConnectionScope } from '../connections/useConnectionScope';
import { coursesGateway, type Course } from '../courses/courses-gateway';
import { CourseCard } from '../courses/components/CourseCard';
import { filterCoursesByLifecycle, getCourseLifecycle, matchesCourseSearch, normalizeCourseEndDatesBySequence, type CourseLifecycle, type CourseLifecycleFilter } from '../courses/course-status';
import { useIgnoredCourses } from '../courses/course-visibility';
import { useTrackedCourses } from '../courses/course-tracking';
import { buildSchoolsTree, countCoursesByCategory, courseCategoryPath, groupCoursesByCategory, normalizeCategoryPath, type TreeNode } from './schools-tree';
import { ReportGenerationPanel } from '../reports/ReportGenerationPanel';

const statusFilters: { value: CourseLifecycleFilter; label: string }[] = [
  { value: 'all', label: 'Todos' },
  { value: 'in_progress', label: 'Em andamento' },
  { value: 'not_started', label: 'Não iniciados' },
  { value: 'finished', label: 'Finalizados' },
];

function TreeBranch({ node, courseGroups, courseCounts, coursesPending, coursesError, editMode, ignoredCourseIds, trackedCourseIds, onRestore, onTrack, onUntrack, selectionMode, selectedCourseIds, onToggleCourse, onToggleCategory, level = 0 }: { node: TreeNode; courseGroups: Map<string, Course[]>; courseCounts: Map<string, number>; coursesPending: boolean; coursesError: boolean; editMode: boolean; ignoredCourseIds: Set<string>; trackedCourseIds: Set<string>; onRestore: (courseId: string) => void; onTrack: (courseId: string) => void; onUntrack: (courseId: string) => void; selectionMode: boolean; selectedCourseIds: Set<string>; onToggleCourse: (courseId: string) => void; onToggleCategory: (courseIds: string[]) => void; level?: number }) {
  const [open, setOpen] = useState(false);
  const categoryKey = normalizeCategoryPath(node.path);
  const categoryCourses = courseGroups.get(categoryKey) ?? [];
  const hasChildren = node.children.size > 0;
  const unitLabel = level === 0 ? 'curso' : level === 1 ? 'turma' : 'disciplina';
  const courseCount = courseCounts.get(categoryKey) ?? (coursesPending ? node.count : 0);
  const nodeCourses = [...courseGroups.entries()]
    .filter(([path]) => path === categoryKey || path.startsWith(`${categoryKey} > `))
    .flatMap(([, courses]) => courses);
  const nodeCourseIds = nodeCourses.map((course) => course.courseId);
  const allSelected = nodeCourseIds.length > 0 && nodeCourseIds.every((courseId) => selectedCourseIds.has(courseId));

  return (
    <details className={`${level === 0 ? 'rounded-lg border bg-card' : 'border-l pl-4'} group`} open={open} onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary className="flex cursor-pointer list-none items-center gap-3 px-4 py-3 font-medium marker:hidden hover:bg-muted/50">{selectionMode && nodeCourseIds.length > 0 && <input type="checkbox" checked={allSelected} aria-label={`Selecionar todos os cursos de ${node.name}`} className="h-4 w-4 shrink-0 accent-primary" onChange={() => onToggleCategory(nodeCourseIds)} onClick={(event) => event.stopPropagation()} />}<span className="flex h-8 w-8 items-center justify-center rounded-full bg-primary/10 text-primary">{level === 0 ? <Building2 className="h-4 w-4" /> : <BookOpen className="h-4 w-4" />}</span><span className="min-w-0 flex-1"><span className="block truncate">{node.name}</span><span className="mt-0.5 block text-xs font-normal text-muted-foreground">{courseCount} {unitLabel}{courseCount === 1 ? '' : 's'}</span></span><ChevronDown className="h-4 w-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-180" /></summary>
      <div className="space-y-3 px-4 pb-4">
        {[...node.children.values()].sort((left, right) => left.name.localeCompare(right.name, 'pt-BR')).map((child) => <TreeBranch key={child.path} node={child} courseGroups={courseGroups} courseCounts={courseCounts} coursesPending={coursesPending} coursesError={coursesError} editMode={editMode} ignoredCourseIds={ignoredCourseIds} trackedCourseIds={trackedCourseIds} onRestore={onRestore} onTrack={onTrack} onUntrack={onUntrack} selectionMode={selectionMode} selectedCourseIds={selectedCourseIds} onToggleCourse={onToggleCourse} onToggleCategory={onToggleCategory} level={level + 1} />)}
        {!hasChildren && coursesPending && <Skeleton className="h-40 rounded-lg" />}
        {!hasChildren && coursesError && <p className="p-3 text-sm text-destructive">Não foi possível carregar as unidades curriculares.</p>}
        {!hasChildren && !coursesPending && !coursesError && categoryCourses.length === 0 && <p className="p-3 text-sm text-muted-foreground">Nenhum curso corresponde ao filtro selecionado.</p>}
        {!hasChildren && categoryCourses.length > 0 && <div className="grid gap-4 pt-1 md:grid-cols-2 xl:grid-cols-3">{categoryCourses.map((course) => {
          const ignored = ignoredCourseIds.has(course.courseId);
          const tracked = trackedCourseIds.has(course.courseId);
          const active = getCourseLifecycle(course) === 'in_progress';
          const canAdd = ignored || (!active && !tracked);
          const action = editMode && canAdd
            ? { label: 'Adicionar aos Meus Cursos', ariaLabel: `Adicionar ${course.displayName ?? course.fullName} aos Meus Cursos`, icon: <Plus className="h-4 w-4" />, onClick: () => { if (ignored) onRestore(course.courseId); if (!active) onTrack(course.courseId); } }
            : editMode && tracked && !active
              ? { label: 'Remover dos Meus Cursos', ariaLabel: `Remover ${course.displayName ?? course.fullName} dos Meus Cursos`, icon: <EyeOff className="h-4 w-4" />, onClick: () => onUntrack(course.courseId) }
              : undefined;
          return <CourseCard key={`${course.connectionRef}:${course.courseId}`} course={course} selection={selectionMode ? { checked: selectedCourseIds.has(course.courseId), ariaLabel: `Selecionar ${course.displayName ?? course.fullName} para o relatório`, onChange: () => onToggleCourse(course.courseId) } : undefined} action={action} />;
        })}</div>}
      </div>
    </details>
  );
}

export function SchoolsPage() {
  const { connectionRef } = useConnectionScope();
  const { editMode } = useEditMode();
  const { ignoredCourseIds, restoreCourse } = useIgnoredCourses(connectionRef);
  const { trackedCourseIds, trackCourse, untrackCourse } = useTrackedCourses(connectionRef);
  const [search, setSearch] = useState('');
  const [selectedStatuses, setSelectedStatuses] = useState<CourseLifecycle[]>([]);
  const [selectionMode, setSelectionMode] = useState(false);
  const [selectedCourseIds, setSelectedCourseIds] = useState<Set<string>>(new Set());
  const query = useQuery({ queryKey: ['app', 'schools', 'hierarchy', connectionRef], queryFn: () => coursesGateway.hierarchy(connectionRef), staleTime: 60_000 });
  const coursesQuery = useQuery({ queryKey: ['app', 'courses', 'all-pages', connectionRef], queryFn: () => coursesGateway.listAll(connectionRef, 100), staleTime: 60_000 });
  const allCourses = useMemo(() => normalizeCourseEndDatesBySequence(coursesQuery.data?.data ?? []), [coursesQuery.data?.data]);
  const visibleCourses = useMemo(() => filterCoursesByLifecycle(allCourses, selectedStatuses).filter((course) => matchesCourseSearch(course, search)), [allCourses, search, selectedStatuses]);
  const courseGroups = useMemo(() => groupCoursesByCategory(visibleCourses), [visibleCourses]);
  const courseCounts = useMemo(() => countCoursesByCategory(visibleCourses), [visibleCourses]);
  const tree = useMemo(() => {
    const items = query.data?.data ?? [];
    if (!coursesQuery.isSuccess) return buildSchoolsTree(items);
    const visibleCategoryPaths = new Set(visibleCourses.map((course) => normalizeCategoryPath(courseCategoryPath(course))));
    const matchingItems = items.filter((item) => {
      const itemPath = normalizeCategoryPath(item.path);
      return [...visibleCategoryPaths].some((categoryPath) => categoryPath === itemPath || categoryPath.startsWith(`${itemPath} > `));
    });
    return buildSchoolsTree(matchingItems);
  }, [coursesQuery.isSuccess, query.data?.data, visibleCourses]);
  const selectedCourses = useMemo(() => visibleCourses.filter((course) => selectedCourseIds.has(course.courseId)), [selectedCourseIds, visibleCourses]);
  const toggleCourse = (courseId: string) => setSelectedCourseIds((current) => {
    const next = new Set(current);
    if (next.has(courseId)) next.delete(courseId); else next.add(courseId);
    return next;
  });
  const toggleCategory = (courseIds: string[]) => setSelectedCourseIds((current) => {
    const next = new Set(current);
    const shouldAdd = courseIds.some((courseId) => !next.has(courseId));
    courseIds.forEach((courseId) => shouldAdd ? next.add(courseId) : next.delete(courseId));
    return next;
  });
  const toggleStatus = (status: CourseLifecycle) => setSelectedStatuses((current) => current.includes(status)
    ? current.filter((item) => item !== status)
    : [...current, status]);
  const toggleSelectionMode = () => {
    setSelectionMode((current) => !current);
    if (selectionMode) setSelectedCourseIds(new Set());
  };

  return (
    <main className="space-y-6 animate-fade-in" aria-labelledby="schools-title">
      <header className="page-heading"><div><p className="eyebrow">CATÁLOGO</p><h1 id="schools-title">Escolas</h1><p>Cursos em andamento já aparecem em Meus Cursos. Use o modo de edição para adicionar outras turmas ao acompanhamento.</p></div><Button type="button" variant={selectionMode ? 'secondary' : 'outline'} size="sm" onClick={toggleSelectionMode}><FileSpreadsheet className="mr-1.5 h-4 w-4" />{selectionMode ? 'Cancelar seleção' : 'Gerar relatório'}</Button></header>
      <div className="flex flex-col gap-3 rounded-lg border bg-card p-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex flex-wrap gap-2" role="group" aria-label="Filtros de status dos cursos nas escolas">
          <button type="button" aria-pressed={selectedStatuses.length === 0} onClick={() => setSelectedStatuses([])} className={`rounded-full border px-3 py-1.5 text-sm transition-colors ${selectedStatuses.length === 0 ? 'border-primary bg-primary text-primary-foreground' : 'border-border bg-card text-muted-foreground hover:border-primary/40 hover:text-foreground'}`}>Todos</button>
          {statusFilters.slice(1).map((filter) => <button key={filter.value} type="button" aria-pressed={selectedStatuses.includes(filter.value as CourseLifecycle)} onClick={() => toggleStatus(filter.value as CourseLifecycle)} className={`rounded-full border px-3 py-1.5 text-sm transition-colors ${selectedStatuses.includes(filter.value as CourseLifecycle) ? 'border-primary bg-primary text-primary-foreground' : 'border-border bg-card text-muted-foreground hover:border-primary/40 hover:text-foreground'}`}>{filter.label}</button>)}
        </div>
        <div className="relative w-full sm:w-72"><Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" /><Input type="search" className="pl-9" placeholder="Buscar escola, curso ou turma" aria-label="Buscar escolas" value={search} onChange={(event) => setSearch(event.target.value)} /></div>
      </div>
      {selectionMode && <ReportGenerationPanel
        connectionRef={connectionRef ?? ''}
        courses={selectedCourses}
        onClear={() => setSelectedCourseIds(new Set())}
        onCompleted={() => {
          setSelectedCourseIds(new Set());
          setSelectionMode(false);
        }}
      />}
      {query.isPending && <div className="space-y-3"><Skeleton className="h-16 rounded-lg" /><Skeleton className="h-16 rounded-lg" /></div>}
      {query.isError && <Card><CardContent className="p-6"><p role="alert">Não foi possível carregar as categorias.</p></CardContent></Card>}
      {coursesQuery.isError && <Card><CardContent className="p-6"><p role="alert">Não foi possível carregar os cursos para aplicar o filtro.</p></CardContent></Card>}
      {query.isSuccess && tree.children.size === 0 && <Card><CardContent className="flex flex-col items-center gap-2 p-12 text-center"><Building2 className="h-10 w-10 text-muted-foreground/50" /><h2 className="font-medium">Nenhuma categoria encontrada</h2></CardContent></Card>}
      {query.isSuccess && tree.children.size > 0 && <div className="space-y-3">{[...tree.children.values()].map((node) => <TreeBranch key={node.path} node={node} courseGroups={courseGroups} courseCounts={courseCounts} coursesPending={coursesQuery.isPending} coursesError={coursesQuery.isError} editMode={editMode} ignoredCourseIds={ignoredCourseIds} trackedCourseIds={trackedCourseIds} onRestore={restoreCourse} onTrack={trackCourse} onUntrack={untrackCourse} selectionMode={selectionMode} selectedCourseIds={selectedCourseIds} onToggleCourse={toggleCourse} onToggleCategory={toggleCategory} />)}</div>}
    </main>
  );
}
