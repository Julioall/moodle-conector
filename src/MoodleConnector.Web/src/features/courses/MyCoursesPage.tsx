import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { BookOpen, Building2, ChevronDown, GraduationCap, Search, UsersRound } from 'lucide-react';

import { Card, CardContent } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { useConnectionScope } from '../connections/useConnectionScope';
import { CourseCard } from './components/CourseCard';
import { coursesGateway, type Course } from './courses-gateway';

type CategoryNode = { name: string; path: string; children: Map<string, CategoryNode>; courses: Course[] };

function buildCategoryTree(courses: Course[]) {
  const root: CategoryNode = { name: 'root', path: '', children: new Map(), courses: [] };
  courses.forEach((course) => {
    const parts = (course.categoryName?.split('>').map((part) => part.trim()).filter(Boolean) ?? []);
    const visibleParts = parts.length > 1 && parts[0].toLocaleLowerCase('pt-BR') === 'senai' ? parts.slice(1) : parts;
    const categoryParts = visibleParts.length > 0 ? visibleParts : ['Sem categoria'];
    let node = root;
    categoryParts.forEach((part) => {
      const path = node.path ? `${node.path} > ${part}` : part;
      if (!node.children.has(part)) node.children.set(part, { name: part, path, children: new Map(), courses: [] });
      node = node.children.get(part)!;
    });
    node.courses.push(course);
  });
  return root;
}

function countCourses(node: CategoryNode): number {
  return node.courses.length + [...node.children.values()].reduce((total, child) => total + countCourses(child), 0);
}

function CategoryBranch({ node, level = 0 }: { node: CategoryNode; level?: number }) {
  const [open, setOpen] = useState(false);
  const children = [...node.children.values()].sort((left, right) => left.name.localeCompare(right.name, 'pt-BR'));
  const courseCount = countCourses(node);
  const Icon = level === 0 ? Building2 : level === 1 ? GraduationCap : UsersRound;
  const hasContent = children.length > 0 || node.courses.length > 0;

  return (
    <section className={level === 0 ? 'overflow-hidden rounded-lg border bg-card' : 'overflow-hidden rounded-lg border bg-muted/30'}>
      <button type="button" className="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors hover:bg-muted/50" onClick={() => setOpen((value) => !value)} aria-expanded={open}>
        <Icon className={level === 0 ? 'h-5 w-5 text-primary' : 'h-4 w-4 text-primary/80'} />
        <span className="min-w-0 flex-1"><span className="block truncate font-semibold">{node.name}</span><span className="mt-0.5 block text-xs font-normal text-muted-foreground">{courseCount} curso{courseCount === 1 ? '' : 's'}</span></span>
        {hasContent && <ChevronDown className={`h-4 w-4 shrink-0 text-muted-foreground transition-transform ${open ? 'rotate-180' : ''}`} />}
      </button>
      {open && <div className="space-y-3 border-t p-4">{children.map((child) => <CategoryBranch key={child.path} node={child} level={level + 1} />)}{node.courses.length > 0 && <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">{node.courses.map((course) => <CourseCard key={`${course.connectionRef}:${course.courseId}`} course={course} />)}</div>}</div>}
    </section>
  );
}

export function MyCoursesPage() {
  const { connectionRef } = useConnectionScope();
  const [search, setSearch] = useState('');
  const query = useQuery({ queryKey: ['app', 'courses', connectionRef], queryFn: () => coursesGateway.listAll(connectionRef, 100), staleTime: 60_000 });
  const tree = useMemo(() => {
    const term = search.trim().toLocaleLowerCase('pt-BR');
    const courses = (query.data?.data ?? []).filter((course) => !term || [course.fullName, course.shortName, course.displayName, course.categoryName].filter(Boolean).some((value) => value!.toLocaleLowerCase('pt-BR').includes(term)));
    return buildCategoryTree(courses);
  }, [query.data?.data, search]);
  const courseCount = query.data?.meta.total ?? query.data?.data.length ?? 0;

  return (
    <main className="space-y-6 animate-fade-in" aria-labelledby="courses-title">
      <header className="page-heading"><div><p className="eyebrow">OPERACIONAL</p><h1 id="courses-title">Meus Cursos</h1><p>{query.isPending ? 'Carregando cursos…' : `${courseCount} curso${courseCount === 1 ? '' : 's'} em acompanhamento`}</p></div><div className="relative w-full sm:w-72"><Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" /><Input type="search" placeholder="Buscar curso..." value={search} onChange={(event) => setSearch(event.target.value)} className="pl-9" /></div></header>
      {query.isPending && <div className="space-y-3"><Skeleton className="h-16 rounded-lg" /><Skeleton className="h-16 rounded-lg" /></div>}
      {query.isError && <Card><CardContent className="p-6 text-sm text-destructive" role="alert">Não foi possível carregar os cursos.</CardContent></Card>}
      {query.isSuccess && tree.children.size === 0 && <Card className="border-dashed"><CardContent className="flex flex-col items-center gap-3 p-12 text-center"><BookOpen className="h-10 w-10 text-muted-foreground/40" /><h2 className="font-medium">Nenhum curso encontrado</h2><p className="text-sm text-muted-foreground">Verifique a conexão selecionada ou ajuste a busca.</p></CardContent></Card>}
      {query.isSuccess && tree.children.size > 0 && <section className="space-y-4" aria-label="Cursos agrupados por categoria">{[...tree.children.values()].sort((left, right) => left.name.localeCompare(right.name, 'pt-BR')).map((node) => <CategoryBranch key={node.path} node={node} />)}</section>}
    </main>
  );
}
