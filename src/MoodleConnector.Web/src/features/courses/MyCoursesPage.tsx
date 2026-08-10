import { useQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/card';
import { Skeleton } from '../../components/ui/skeleton';
import { coursesGateway } from './courses-gateway';

export function MyCoursesPage() {
  const [params, setParams] = useSearchParams();
  const connectionRef = params.get('connectionRef') ?? undefined;
  const query = useQuery({ queryKey: ['portal', 'courses', connectionRef], queryFn: () => coursesGateway.list(connectionRef) });
  return <main className="content-frame courses-page"><header className="page-heading"><div><p className="eyebrow">OPERACIONAL</p><h1>Meus cursos</h1><p>Consulte os cursos disponíveis em cada Moodle.</p></div>{query.data && <span className="freshness">Atualizado em {new Date(query.data.meta.generatedAt).toLocaleString('pt-BR')}</span>}</header>
    <label className="course-filter">Moodle<select aria-label="Filtrar por Moodle" value={connectionRef ?? ''} onChange={e => { const value = e.target.value; if (value) setParams({ connectionRef: value }); else setParams({}); }}><option value="">Moodle Padrão</option>{query.data?.data.map(course => <option key={course.connectionRef} value={course.connectionRef}>{course.connectionRef}</option>)}</select></label>
    {query.isPending && <div className="course-grid"><Skeleton className="course-skeleton" /><Skeleton className="course-skeleton" /></div>}
    {query.isError && <Card><CardContent><p role="alert">Não foi possível carregar os cursos.</p></CardContent></Card>}
    {query.isSuccess && query.data.data.length === 0 && <Card><CardContent><p>Nenhum curso encontrado.</p></CardContent></Card>}
    {query.isSuccess && <section className="course-grid" aria-label="Cursos">{query.data.data.map(course => <Card key={`${course.connectionRef}:${course.courseId}`}><CardHeader><CardTitle>{course.displayName ?? course.fullName}</CardTitle></CardHeader><CardContent><p>{course.shortName ?? course.categoryName ?? 'Curso Moodle'}</p><Link className="ui-button ui-button-outline ui-button-sm" to={`/cursos/${encodeURIComponent(course.connectionRef)}/${encodeURIComponent(course.courseId)}`}>Abrir curso</Link></CardContent></Card>)}</section>}</main>;
}
