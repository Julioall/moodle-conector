import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Building2 } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { connectionDisplayName, useConnectionScope } from '../connections/useConnectionScope';
import { coursesGateway, type Course, type CourseHierarchyNode } from '../courses/courses-gateway';
import { CourseCard } from '../courses/components/CourseCard';

type TreeNode = { name: string; path: string; count: number; children: Map<string, TreeNode> };

function buildTree(items: CourseHierarchyNode[]) {
  const root: TreeNode = { name: 'SENAI', path: '', count: 0, children: new Map() };
  for (const item of items) {
    const parts = item.path.split('>').map((part) => part.trim()).filter(Boolean);
    let node = root;
    for (const part of parts) {
      const path = node.path ? `${node.path} > ${part}` : part;
      if (!node.children.has(part)) node.children.set(part, { name: part, path, count: 0, children: new Map() });
      node = node.children.get(part)!;
    }
    node.count = item.courseCount;
  }
  return root;
}

function TreeBranch({ node, connectionRef, level = 0 }: { node: TreeNode; connectionRef?: string; level?: number }) {
  const [open, setOpen] = useState(level < 2);
  const coursesQuery = useQuery({
    queryKey: ['app', 'schools', 'courses', connectionRef, node.path],
    queryFn: () => coursesGateway.byCategory(node.path, connectionRef, 1, 100),
    enabled: open && node.children.size === 0 && Boolean(node.path),
  });
  const courses: Course[] = coursesQuery.data?.data ?? [];
  const hasChildren = node.children.size > 0;

  return (
    <details className={level === 0 ? 'rounded-lg border bg-card' : 'border-l pl-4'} open={open} onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary className="flex cursor-pointer list-none items-center justify-between gap-3 px-4 py-3 font-medium marker:hidden">
        <span>{node.name}</span>
        <span className="text-xs font-normal text-muted-foreground">{node.count} unidade(s)</span>
      </summary>
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
  const { connectionRef, selectedConnection } = useConnectionScope();
  const [search, setSearch] = useState('');
  const query = useQuery({ queryKey: ['app', 'schools', 'hierarchy', connectionRef], queryFn: () => coursesGateway.hierarchy(connectionRef) });
  const tree = useMemo(() => {
    const term = search.trim().toLocaleLowerCase('pt-BR');
    const items = (query.data?.data ?? []).filter((item) => !term || item.path.toLocaleLowerCase('pt-BR').includes(term));
    return buildTree(items);
  }, [query.data?.data, search]);

  return <main className="space-y-6" aria-labelledby="schools-title">
    <header className="flex flex-col gap-3 border-b pb-5 sm:flex-row sm:items-end sm:justify-between"><div><p className="mb-1 text-[10px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">Organização acadêmica</p><h1 id="schools-title" className="text-2xl font-semibold tracking-tight">Escolas</h1><p className="mt-1 text-sm text-muted-foreground">Navegue pelas categorias e carregue as unidades curriculares sob demanda.</p></div><div className="text-left text-xs text-muted-foreground sm:text-right"><p className="font-medium text-foreground">{connectionDisplayName(selectedConnection)}</p>{query.data?.meta.generatedAt && <p>Atualizado em {new Date(query.data.meta.generatedAt).toLocaleString('pt-BR')}</p>}</div></header>
    <div className="relative"><input className="h-10 w-full rounded-md border bg-background px-3 text-sm" placeholder="Buscar escola, curso ou turma" aria-label="Buscar escolas" value={search} onChange={(event) => setSearch(event.target.value)} /></div>
    {query.isPending && <div className="space-y-3"><Skeleton className="h-16 rounded-lg" /><Skeleton className="h-16 rounded-lg" /></div>}
    {query.isError && <Card><CardContent className="p-6"><p role="alert">Não foi possível carregar as categorias.</p></CardContent></Card>}
    {query.isSuccess && tree.children.size === 0 && <Card><CardContent className="flex flex-col items-center gap-2 p-12 text-center"><Building2 className="h-10 w-10 text-muted-foreground/50" /><h2 className="font-medium">Nenhuma categoria encontrada</h2></CardContent></Card>}
    {query.isSuccess && tree.children.size > 0 && <div className="space-y-3">{[...tree.children.values()].map((node) => <TreeBranch key={node.path} node={node} connectionRef={connectionRef} />)}</div>}
  </main>;
}
