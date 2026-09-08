import { useCallback, useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { BookOpen, Building2, ChevronDown, EyeOff, FileSpreadsheet, Plus, Search } from 'lucide-react';

import { Card, CardContent } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
import { useEditMode } from '@/components/layout/edit-mode-context';
import { useConnectionScope } from '../connections/useConnectionScope';
import { coursesGateway, type Course } from '../courses/courses-gateway';
import { CourseCard } from '../courses/components/CourseCard';
import { filterCoursesByLifecycle, getCourseLifecycle, normalizeCourseEndDatesBySequence, type CourseLifecycle, type CourseLifecycleFilter } from '../courses/course-status';
import { useIgnoredCourses } from '../courses/course-visibility';
import { useTrackedCourses } from '../courses/course-tracking';
import { buildSchoolsTree, courseCategoryPath, groupCoursesByCategory, normalizeCategoryPath, type TreeNode } from './schools-tree';
import { ReportGenerationPanel } from '../reports/ReportGenerationPanel';

const statusFilters: { value: CourseLifecycleFilter; label: string }[] = [
  { value: 'all', label: 'Todos' },
  { value: 'in_progress', label: 'Em andamento' },
  { value: 'not_started', label: 'Não iniciados' },
  { value: 'finished', label: 'Finalizados' },
];

function TreeBranch({ node, connectionRef, courseGroups, loadedCourses, onCoursesLoaded, editMode, ignoredCourseIds, trackedCourseIds, onRestore, onTrack, onUntrack, selectionMode, selectedCourseIds, onToggleCourse, onToggleCategory, level = 0 }: { node: TreeNode; connectionRef?: string; courseGroups: Map<string, Course[]>; loadedCourses: Course[]; onCoursesLoaded: (courses: Course[]) => void; editMode: boolean; ignoredCourseIds: Set<string>; trackedCourseIds: Set<string>; onRestore: (courseId: string) => void; onTrack: (courseId: string) => void; onUntrack: (courseId: string) => void; selectionMode: boolean; selectedCourseIds: Set<string>; onToggleCourse: (courseId: string) => void; onToggleCategory: (courseIds: string[]) => void; level?: number }) {
  const [open, setOpen] = useState(false);
  const hasChildren = node.children.size > 0;
  const categoryKey = normalizeCategoryPath(node.path);
  const coursesQuery = useQuery({
    queryKey: ['app', 'school-courses', connectionRef, node.path],
    queryFn: () => coursesGateway.listAllByCategory(node.path, connectionRef, 100),
    enabled: Boolean(connectionRef && open && !hasChildren),
    staleTime: 60_000,
    refetchInterval: (currentQuery) => currentQuery.state.data?.meta.complete === false || currentQuery.state.data?.meta.refreshQueued ? 15_000 : false,
  });
  useEffect(() => {
    if (coursesQuery.data?.data) onCoursesLoaded(coursesQuery.data.data);
  }, [coursesQuery.data?.data, onCoursesLoaded]);

  const categoryCourses = courseGroups.get(categoryKey) ?? [];
  const nodeCourses = loadedCourses.filter((course) => {
    const path = normalizeCategoryPath(courseCategoryPath(course));
    return path === categoryKey || path.startsWith(`${categoryKey} > `);
  });
  const nodeCourseIds = nodeCourses.map((course) => course.courseId);
  const allSelected = nodeCourseIds.length > 0 && nodeCourseIds.every((courseId) => selectedCourseIds.has(courseId));
  const unitLabel = level === 0 ? 'curso' : level === 1 ? 'turma' : 'disciplina';
  const courseCount = !hasChildren && coursesQuery.data ? categoryCourses.length : node.count;

  return (
    <details className={`${level === 0 ? 'rounded-lg border bg-card' : 'border-l pl-4'} group`} open={open} onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary className="flex cursor-pointer list-none items-center gap-3 px-4 py-3 font-medium marker:hidden hover:bg-muted/50">{selectionMode && nodeCourseIds.length > 0 && <input type="checkbox" checked={allSelected} aria-label={`Selecionar todos os cursos de ${node.name}`} className="h-4 w-4 shrink-0 accent-primary" onChange={() => onToggleCategory(nodeCourseIds)} onClick={(event) => event.stopPropagation()} />}<span className="flex h-8 w-8 items-center justify-center rounded-full bg-primary/10 text-primary">{level === 0 ? <Building2 className="h-4 w-4" /> : <BookOpen className="h-4 w-4" />}</span><span className="min-w-0 flex-1"><span className="block truncate">{node.name}</span><span className="mt-0.5 block text-xs font-normal text-muted-foreground">{courseCount} {unitLabel}{courseCount === 1 ? '' : 's'}</span></span><ChevronDown className="h-4 w-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-180" /></summary>
      <div className="space-y-3 px-4 pb-4">
        {open && <>
          {[...node.children.values()].sort((left, right) => left.name.localeCompare(right.name, 'pt-BR')).map((child) => <TreeBranch key={child.path} node={child} connectionRef={connectionRef} courseGroups={courseGroups} loadedCourses={loadedCourses} onCoursesLoaded={onCoursesLoaded} editMode={editMode} ignoredCourseIds={ignoredCourseIds} trackedCourseIds={trackedCourseIds} onRestore={onRestore} onTrack={onTrack} onUntrack={onUntrack} selectionMode={selectionMode} selectedCourseIds={selectedCourseIds} onToggleCourse={onToggleCourse} onToggleCategory={onToggleCategory} level={level + 1} />)}
          {!hasChildren && coursesQuery.isPending && <Skeleton className="h-40 rounded-lg" />}
          {!hasChildren && coursesQuery.isError && <p className="p-3 text-sm text-destructive">Não foi possível carregar as unidades curriculares.</p>}
          {!hasChildren && !coursesQuery.isPending && !coursesQuery.isError && categoryCourses.length === 0 && <p className="p-3 text-sm text-muted-foreground">Nenhum curso corresponde ao filtro selecionado.</p>}
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
        </>}
      </div>
    </details>
  );
}

export function SchoolsPage() {
  const { connectionRef, connections } = useConnectionScope();
  const { editMode } = useEditMode();
  const { ignoredCourseIds, restoreCourse } = useIgnoredCourses(connectionRef);
  const { trackedCourseIds, trackCourse, untrackCourse } = useTrackedCourses(connectionRef);
  const [search, setSearch] = useState('');
  const [selectedStatuses, setSelectedStatuses] = useState<CourseLifecycle[]>([]);
  const [selectionMode, setSelectionMode] = useState(false);
  const [selectedCourseIds, setSelectedCourseIds] = useState<Set<string>>(new Set());
  const [loadedCourses, setLoadedCourses] = useState<Course[]>([]);
  const query = useQuery({
    queryKey: ['app', 'schools', 'hierarchy', connectionRef],
    queryFn: () => coursesGateway.hierarchy(connectionRef),
    enabled: Boolean(connectionRef),
    staleTime: 60_000,
    refetchInterval: (currentQuery) => currentQuery.state.data?.meta.complete === false || currentQuery.state.data?.meta.refreshQueued ? 15_000 : false,
  });
  const normalizedSearch = search.trim();
  const searchQuery = useQuery({
    queryKey: ['app', 'school-course-search', connectionRef, normalizedSearch],
    queryFn: () => coursesGateway.search(normalizedSearch, connectionRef, 100),
    enabled: Boolean(connectionRef && normalizedSearch),
    staleTime: 60_000,
    refetchInterval: (currentQuery) => currentQuery.state.data?.meta.complete === false || currentQuery.state.data?.meta.refreshQueued ? 15_000 : false,
  });
  useEffect(() => {
    setLoadedCourses([]);
    setSelectedCourseIds(new Set());
  }, [connectionRef]);
  const onCoursesLoaded = useCallback((courses: Course[]) => {
    setLoadedCourses((current) => {
      const byId = new Map(current.map((course) => [course.courseId, course]));
      courses.forEach((course) => byId.set(course.courseId, course));
      return [...byId.values()];
    });
  }, []);
  useEffect(() => {
    if (searchQuery.data?.data) onCoursesLoaded(searchQuery.data.data);
  }, [onCoursesLoaded, searchQuery.data?.data]);
  const catalogCourses = normalizedSearch ? searchQuery.data?.data ?? [] : loadedCourses;
  const visibleCourses = useMemo(() => filterCoursesByLifecycle(normalizeCourseEndDatesBySequence(catalogCourses), selectedStatuses), [catalogCourses, selectedStatuses]);
  const courseGroups = useMemo(() => groupCoursesByCategory(visibleCourses), [visibleCourses]);
  const catalogRefreshing = query.data?.meta.complete === false || query.data?.meta.refreshQueued === true || Boolean(normalizedSearch && (searchQuery.isPending || searchQuery.data?.meta.complete === false || searchQuery.data?.meta.refreshQueued === true));
  const tree = useMemo(() => {
    const items = query.data?.data ?? [];
    if (!normalizedSearch) return buildSchoolsTree(items);
    const visibleCategoryPaths = new Set(visibleCourses.map((course) => normalizeCategoryPath(courseCategoryPath(course))));
    return buildSchoolsTree(items.filter((item) => {
      const itemPath = normalizeCategoryPath(item.path);
      return [...visibleCategoryPaths].some((categoryPath) => categoryPath === itemPath || categoryPath.startsWith(`${itemPath} > `));
    }));
  }, [normalizedSearch, query.data?.data, visibleCourses]);
  const selectedCourses = useMemo(() => loadedCourses.filter((course) => selectedCourseIds.has(course.courseId)), [loadedCourses, selectedCourseIds]);
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
      {(connections.isPending || query.isPending) && <div className="space-y-3"><Skeleton className="h-16 rounded-lg" /><Skeleton className="h-16 rounded-lg" /></div>}
      {query.isError && <Card><CardContent className="p-6"><p role="alert">Não foi possível carregar as categorias.</p></CardContent></Card>}
      {catalogRefreshing && <p className="text-sm text-muted-foreground" role="status">Preparando cursos e categorias do Moodle…</p>}
      {query.isSuccess && !catalogRefreshing && tree.children.size === 0 && <Card><CardContent className="flex flex-col items-center gap-2 p-12 text-center"><Building2 className="h-10 w-10 text-muted-foreground/50" /><h2 className="font-medium">Nenhuma categoria encontrada</h2></CardContent></Card>}
      {query.isSuccess && tree.children.size > 0 && <div className="space-y-3">{[...tree.children.values()].map((node) => <TreeBranch key={node.path} node={node} connectionRef={connectionRef} courseGroups={courseGroups} loadedCourses={visibleCourses} onCoursesLoaded={onCoursesLoaded} editMode={editMode} ignoredCourseIds={ignoredCourseIds} trackedCourseIds={trackedCourseIds} onRestore={restoreCourse} onTrack={trackCourse} onUntrack={untrackCourse} selectionMode={selectionMode} selectedCourseIds={selectedCourseIds} onToggleCourse={toggleCourse} onToggleCategory={toggleCategory} />)}</div>}
    </main>
  );
}
