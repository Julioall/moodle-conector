import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { BookOpen, Building2, ChevronDown, Search } from 'lucide-react';

import { Card, CardContent } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { useConnectionScope } from '../connections/useConnectionScope';
import { coursesGateway, type Course } from '../courses/courses-gateway';
import { CourseCard } from '../courses/components/CourseCard';
import { buildSchoolsTree, type TreeNode } from './schools-tree';

function TreeBranch({ node, connectionRef, level = 0 }: { node: TreeNode; connectionRef?: string; level?: number }) {
  const [open, setOpen] = useState(false);
  const coursesQuery = useQuery({ queryKey: ['app', 'schools', 'courses', connectionRef, node.path], queryFn: () => coursesGateway.byCategory(node.path, connectionRef, 1, 100), enabled: open && node.children.size === 0 && Boolean(node.path), staleTime: 60_000 });
  const courses: Course[] = coursesQuery.data?.data ?? [];
  const hasChildren = node.children.size > 0;
  const unitLabel = level === 0 ? 'curso' : level === 1 ? 'turma' : 'disciplina';

  return (
    <details className={`${level === 0 ? 'rounded-lg border bg-card' : 'border-l pl-4'} group`} open={open} onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary className="flex cursor-pointer list-none items-center gap-3 px-4 py-3 font-medium marker:hidden hover:bg-muted/50"><span className="flex h-8 w-8 items-center justify-center rounded-full bg-primary/10 text-primary">{level === 0 ? <Building2 className="h-4 w-4" /> : <BookOpen className="h-4 w-4" />}</span><span className="min-w-0 flex-1"><span className="block truncate">{node.name}</span><span className="mt-0.5 block text-xs font-normal text-muted-foreground">{node.count} {unitLabel}{node.count === 1 ? '' : 's'}</span></span><ChevronDown className="h-4 w-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-180" /></summary>
      <div className="space-y-3 px-4 pb-4">
        {[...node.children.values()].sort((left, right) => left.name.localeCompare(right.name, 'pt-BR')).map((child) => <TreeBranch key={child.path} node={child} connectionRef={connectionRef} level={level + 1} />)}
        {!hasChildren && coursesQuery.isPending && <Skeleton className="h-40 rounded-lg" />}
        {!hasChildren && coursesQuery.isError && <p className="p-3 text-sm text-destructive">Não foi possível carregar as unidades curriculares.</p>}
        {!hasChildren && courses.length > 0 && <div className="grid gap-4 pt-1 md:grid-cols-2 xl:grid-cols-3">{courses.map((course) => <CourseCard key={`${course.connectionRef}:${course.courseId}`} course={course} />)}</div>}
      </div>
    </details>
  );
}

export function SchoolsPage() {
  const { connectionRef } = useConnectionScope();
  const [search, setSearch] = useState('');
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
      {query.isPending && <div className="space-y-3"><Skeleton className="h-16 rounded-lg" /><Skeleton className="h-16 rounded-lg" /></div>}
      {query.isError && <Card><CardContent className="p-6"><p role="alert">Não foi possível carregar as categorias.</p></CardContent></Card>}
      {query.isSuccess && tree.children.size === 0 && <Card><CardContent className="flex flex-col items-center gap-2 p-12 text-center"><Building2 className="h-10 w-10 text-muted-foreground/50" /><h2 className="font-medium">Nenhuma categoria encontrada</h2></CardContent></Card>}
      {query.isSuccess && tree.children.size > 0 && <div className="space-y-3">{[...tree.children.values()].map((node) => <TreeBranch key={node.path} node={node} connectionRef={connectionRef} />)}</div>}
    </main>
  );
}
