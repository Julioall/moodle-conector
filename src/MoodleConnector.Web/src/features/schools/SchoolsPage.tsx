import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { BookOpen, Building2, ChevronDown, Search } from 'lucide-react';

import { Card, CardContent } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { useConnectionScope } from '../connections/useConnectionScope';
import { coursesGateway, type Course } from '../courses/courses-gateway';
import { CourseCard } from '../courses/components/CourseCard';
import { filterCoursesByLifecycle, type CourseLifecycleFilter } from '../courses/course-status';
import { buildSchoolsTree, type TreeNode } from './schools-tree';

const statusFilters: { value: CourseLifecycleFilter; label: string }[] = [
  { value: 'in_progress', label: 'Em andamento' },
  { value: 'all', label: 'Todos' },
  { value: 'not_started', label: 'Não iniciados' },
  { value: 'finished', label: 'Finalizados' },
];

function TreeBranch({ node, connectionRef, statusFilter, level = 0 }: { node: TreeNode; connectionRef?: string; statusFilter: CourseLifecycleFilter; level?: number }) {
  const [open, setOpen] = useState(false);
  const coursesQuery = useQuery({ queryKey: ['app', 'schools', 'courses', connectionRef, node.path], queryFn: () => coursesGateway.listAllByCategory(node.path, connectionRef, 100), enabled: open && node.children.size === 0 && Boolean(node.path), staleTime: 60_000 });
  const courses: Course[] = coursesQuery.data?.data ?? [];
  const filteredCourses = filterCoursesByLifecycle(courses, statusFilter);
  const hasChildren = node.children.size > 0;
  const unitLabel = level === 0 ? 'curso' : level === 1 ? 'turma' : 'disciplina';

  return (
    <details className={`${level === 0 ? 'rounded-lg border bg-card' : 'border-l pl-4'} group`} open={open} onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary className="flex cursor-pointer list-none items-center gap-3 px-4 py-3 font-medium marker:hidden hover:bg-muted/50"><span className="flex h-8 w-8 items-center justify-center rounded-full bg-primary/10 text-primary">{level === 0 ? <Building2 className="h-4 w-4" /> : <BookOpen className="h-4 w-4" />}</span><span className="min-w-0 flex-1"><span className="block truncate">{node.name}</span><span className="mt-0.5 block text-xs font-normal text-muted-foreground">{node.count} {unitLabel}{node.count === 1 ? '' : 's'}</span></span><ChevronDown className="h-4 w-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-180" /></summary>
      <div className="space-y-3 px-4 pb-4">
        {[...node.children.values()].sort((left, right) => left.name.localeCompare(right.name, 'pt-BR')).map((child) => <TreeBranch key={child.path} node={child} connectionRef={connectionRef} statusFilter={statusFilter} level={level + 1} />)}
        {!hasChildren && coursesQuery.isPending && <Skeleton className="h-40 rounded-lg" />}
        {!hasChildren && coursesQuery.isError && <p className="p-3 text-sm text-destructive">Não foi possível carregar as unidades curriculares.</p>}
        {!hasChildren && coursesQuery.isSuccess && filteredCourses.length === 0 && <p className="p-3 text-sm text-muted-foreground">Nenhum curso corresponde ao filtro selecionado.</p>}
        {!hasChildren && filteredCourses.length > 0 && <div className="grid gap-4 pt-1 md:grid-cols-2 xl:grid-cols-3">{filteredCourses.map((course) => <CourseCard key={`${course.connectionRef}:${course.courseId}`} course={course} />)}</div>}
      </div>
    </details>
  );
}

export function SchoolsPage() {
  const { connectionRef } = useConnectionScope();
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<CourseLifecycleFilter>('in_progress');
  const query = useQuery({ queryKey: ['app', 'schools', 'hierarchy', connectionRef], queryFn: () => coursesGateway.hierarchy(connectionRef), staleTime: 60_000 });
  const tree = useMemo(() => {
    const term = search.trim().toLocaleLowerCase('pt-BR');
    const items = (query.data?.data ?? []).filter((item) => !term || item.path.toLocaleLowerCase('pt-BR').includes(term));
    return buildSchoolsTree(items);
  }, [query.data?.data, search]);

  return (
    <main className="space-y-6 animate-fade-in" aria-labelledby="schools-title">
      <header className="page-heading">
        <div><p className="eyebrow">CATÁLOGO</p><h1 id="schools-title">Escolas</h1><p>Navegue pelas categorias e carregue as unidades curriculares sob demanda.</p></div>
        <div className="relative w-full md:w-72"><Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" /><Input className="pl-9" placeholder="Buscar escola, curso ou turma" aria-label="Buscar escolas" value={search} onChange={(event) => setSearch(event.target.value)} /></div>
      </header>
      <div className="flex flex-wrap gap-2" role="tablist" aria-label="Status dos cursos nas escolas">{statusFilters.map((filter) => <button key={filter.value} type="button" role="tab" aria-selected={statusFilter === filter.value} onClick={() => setStatusFilter(filter.value)} className={`rounded-full border px-3 py-1.5 text-sm transition-colors ${statusFilter === filter.value ? 'border-primary bg-primary text-primary-foreground' : 'border-border bg-card text-muted-foreground hover:border-primary/40 hover:text-foreground'}`}>{filter.label}</button>)}</div>
      {query.isPending && <div className="space-y-3"><Skeleton className="h-16 rounded-lg" /><Skeleton className="h-16 rounded-lg" /></div>}
      {query.isError && <Card><CardContent className="p-6"><p role="alert">Não foi possível carregar as categorias.</p></CardContent></Card>}
      {query.isSuccess && tree.children.size === 0 && <Card><CardContent className="flex flex-col items-center gap-2 p-12 text-center"><Building2 className="h-10 w-10 text-muted-foreground/50" /><h2 className="font-medium">Nenhuma categoria encontrada</h2></CardContent></Card>}
      {query.isSuccess && tree.children.size > 0 && <div className="space-y-3">{[...tree.children.values()].map((node) => <TreeBranch key={node.path} node={node} connectionRef={connectionRef} statusFilter={statusFilter} />)}</div>}
    </main>
  );
}
