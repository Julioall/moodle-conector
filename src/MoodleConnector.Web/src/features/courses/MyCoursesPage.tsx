import { useQuery } from '@tanstack/react-query';
import { BookOpen, ChevronLeft, ChevronRight } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Card, CardContent } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { connectionDisplayName, useConnectionScope } from '../connections/useConnectionScope';
import { CourseCard } from './components/CourseCard';
import { coursesGateway, type CourseScope } from './courses-gateway';
import { Button } from '@/components/ui/button';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';

const PAGE_SIZE = 20;

export function MyCoursesPage() {
  const { connectionRef, selectedConnection } = useConnectionScope();
  const [scope, setScope] = useState<CourseScope>('active');
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [connectionRef, scope]);
  const query = useQuery({
    queryKey: ['portal', 'courses', connectionRef, scope, page],
    queryFn: () => coursesGateway.list(connectionRef, page, PAGE_SIZE, scope),
  });
  const total = query.data?.meta.total ?? 0;
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <main className="space-y-6" aria-labelledby="courses-title">
      <header className="flex flex-col gap-3 border-b pb-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="mb-1 text-[10px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">Operacional</p>
          <h1 id="courses-title" className="text-2xl font-semibold tracking-tight">Meus cursos</h1>
          <p className="mt-1 text-sm text-muted-foreground">Acompanhe os cursos disponíveis no Moodle selecionado.</p>
        </div>
        <div className="flex flex-col gap-2 text-left text-xs text-muted-foreground sm:items-end sm:text-right">
          <p className="font-medium text-foreground">{connectionDisplayName(selectedConnection)}</p>
          {query.data?.meta.generatedAt && <p>Atualizado em {new Date(query.data.meta.generatedAt).toLocaleString('pt-BR')}</p>}
          <Select value={scope} onValueChange={(value) => setScope(value as CourseScope)}>
            <SelectTrigger className="h-9 w-44 text-xs" aria-label="Escopo dos cursos"><SelectValue /></SelectTrigger>
            <SelectContent><SelectItem value="active">Em andamento</SelectItem><SelectItem value="all">Todos os cursos</SelectItem></SelectContent>
          </Select>
        </div>
      </header>

      {query.isPending && (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3" aria-label="Carregando cursos">
          {[1, 2, 3].map((item) => <Skeleton key={item} className="h-[360px] rounded-lg" />)}
        </div>
      )}

      {query.isError && <Card><CardContent className="p-6"><p role="alert">Não foi possível carregar os cursos.</p></CardContent></Card>}

      {query.isSuccess && query.data.data.length === 0 && (
        <Card><CardContent className="flex flex-col items-center gap-2 p-12 text-center">
          <BookOpen className="h-10 w-10 text-muted-foreground/50" />
          <h2 className="font-medium">Nenhum curso encontrado</h2>
          <p className="text-sm text-muted-foreground">Verifique a conexão selecionada ou atualize as conexões Moodle.</p>
        </CardContent></Card>
      )}

      {query.isSuccess && query.data.data.length > 0 && (
        <>
          <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-3" aria-label="Cursos">
            {query.data.data.map((course) => <CourseCard key={`${course.connectionRef}:${course.courseId}`} course={course} />)}
          </section>
          {totalPages > 1 && <nav className="flex items-center justify-between border-t pt-4" aria-label="Paginação de cursos">
            <p className="text-xs text-muted-foreground">{total} cursos · página {page} de {totalPages}</p>
            <div className="flex gap-2"><Button variant="outline" size="sm" onClick={() => setPage((current) => Math.max(1, current - 1))} disabled={page === 1 || query.isFetching}><ChevronLeft className="h-4 w-4" /> Anterior</Button><Button variant="outline" size="sm" onClick={() => setPage((current) => Math.min(totalPages, current + 1))} disabled={page >= totalPages || query.isFetching}>Próxima <ChevronRight className="h-4 w-4" /></Button></div>
          </nav>}
        </>
      )}
    </main>
  );
}
