import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { BookOpen } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { connectionDisplayName, useConnectionScope } from '../connections/useConnectionScope';
import { CourseCard } from './components/CourseCard';
import { coursesGateway } from './courses-gateway';

export function MyCoursesPage() {
  const { connectionRef, selectedConnection } = useConnectionScope();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const pageSize = 20;
  const query = useQuery({
    queryKey: ['app', 'courses', connectionRef, page],
    queryFn: () => coursesGateway.list(connectionRef, page, pageSize),
  });
  const filteredCourses = useMemo(() => (query.data?.data ?? []).filter((course) => {
    const normalizedSearch = search.trim().toLocaleLowerCase('pt-BR');
    return !normalizedSearch || [course.fullName, course.shortName, course.displayName].filter(Boolean).some((value) => value!.toLocaleLowerCase('pt-BR').includes(normalizedSearch));
  }), [query.data?.data, search]);

  return (
    <main className="space-y-6" aria-labelledby="courses-title">
      <header className="flex flex-col gap-3 border-b pb-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="mb-1 text-[10px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">Operacional</p>
          <h1 id="courses-title" className="text-2xl font-semibold tracking-tight">Meus cursos</h1>
          <p className="mt-1 text-sm text-muted-foreground">Acompanhe os cursos disponíveis no Moodle selecionado.</p>
        </div>
        <div className="text-left text-xs text-muted-foreground sm:text-right">
          <p className="font-medium text-foreground">{connectionDisplayName(selectedConnection)}</p>
          {query.data?.meta.generatedAt && <p>Atualizado em {new Date(query.data.meta.generatedAt).toLocaleString('pt-BR')}</p>}
        </div>
      </header>

      <section className="rounded-lg border bg-card p-4" aria-label="Busca de cursos">
        <input className="h-10 w-full rounded-md border bg-background px-3 text-sm" placeholder="Buscar por nome ou código" aria-label="Buscar cursos" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} />
      </section>

      {query.isPending && (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3" aria-label="Carregando cursos">
          {[1, 2, 3].map((item) => <Skeleton key={item} className="h-[360px] rounded-lg" />)}
        </div>
      )}

      {query.isError && <Card><CardContent className="p-6"><p role="alert">Não foi possível carregar os cursos.</p></CardContent></Card>}

      {query.isSuccess && filteredCourses.length === 0 && (
        <Card><CardContent className="flex flex-col items-center gap-2 p-12 text-center">
          <BookOpen className="h-10 w-10 text-muted-foreground/50" />
          <h2 className="font-medium">Nenhum curso encontrado</h2>
          <p className="text-sm text-muted-foreground">Verifique a conexão selecionada ou atualize as conexões Moodle.</p>
        </CardContent></Card>
      )}

      {query.isSuccess && filteredCourses.length > 0 && (
        <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-3" aria-label="Cursos">
          {filteredCourses.map((course) => <CourseCard key={`${course.connectionRef}:${course.courseId}`} course={course} />)}
        </section>
      )}

      {query.isSuccess && (query.data.meta.total ?? 0) > 0 && <nav className="flex items-center justify-between border-t pt-4" aria-label="Paginação de cursos">
        <span className="text-sm text-muted-foreground">Página {query.data.meta.page} · {query.data.meta.total} cursos</span>
        <div className="flex gap-2">
          <button type="button" className="rounded-md border px-3 py-2 text-sm disabled:opacity-50" disabled={page <= 1 || query.isFetching} onClick={() => setPage((current) => Math.max(1, current - 1))}>Anterior</button>
          <button type="button" className="rounded-md border px-3 py-2 text-sm disabled:opacity-50" disabled={!query.data.meta.hasMore || query.isFetching} onClick={() => setPage((current) => current + 1)}>Próxima</button>
        </div>
      </nav>}
    </main>
  );
}

