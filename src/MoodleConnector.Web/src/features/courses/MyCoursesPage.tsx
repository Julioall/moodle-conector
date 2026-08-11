import { useQuery } from '@tanstack/react-query';
import { BookOpen } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { connectionDisplayName, useConnectionScope } from '../connections/useConnectionScope';
import { CourseCard } from './components/CourseCard';
import { coursesGateway } from './courses-gateway';

export function MyCoursesPage() {
  const { connectionRef, selectedConnection } = useConnectionScope();
  const query = useQuery({
    queryKey: ['app', 'courses', connectionRef],
    queryFn: () => coursesGateway.list(connectionRef),
  });

  return (
    <main className="space-y-6" aria-labelledby="courses-title">
      <header className="flex flex-col gap-3 border-b pb-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="mb-1 text-[10px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">Operacional</p>
          <h1 id="courses-title" className="text-2xl font-semibold tracking-tight">Meus cursos</h1>
          <p className="mt-1 text-sm text-muted-foreground">Acompanhe os cursos disponÃ­veis no Moodle selecionado.</p>
        </div>
        <div className="text-left text-xs text-muted-foreground sm:text-right">
          <p className="font-medium text-foreground">{connectionDisplayName(selectedConnection)}</p>
          {query.data?.meta.generatedAt && <p>Atualizado em {new Date(query.data.meta.generatedAt).toLocaleString('pt-BR')}</p>}
        </div>
      </header>

      {query.isPending && (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3" aria-label="Carregando cursos">
          {[1, 2, 3].map((item) => <Skeleton key={item} className="h-[360px] rounded-lg" />)}
        </div>
      )}

      {query.isError && <Card><CardContent className="p-6"><p role="alert">NÃ£o foi possÃ­vel carregar os cursos.</p></CardContent></Card>}

      {query.isSuccess && query.data.data.length === 0 && (
        <Card><CardContent className="flex flex-col items-center gap-2 p-12 text-center">
          <BookOpen className="h-10 w-10 text-muted-foreground/50" />
          <h2 className="font-medium">Nenhum curso encontrado</h2>
          <p className="text-sm text-muted-foreground">Verifique a conexÃ£o selecionada ou atualize as conexÃµes Moodle.</p>
        </CardContent></Card>
      )}

      {query.isSuccess && query.data.data.length > 0 && (
        <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-3" aria-label="Cursos">
          {query.data.data.map((course) => <CourseCard key={`${course.connectionRef}:${course.courseId}`} course={course} />)}
        </section>
      )}
    </main>
  );
}

