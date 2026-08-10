import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/card';
import { RiskBadge } from './components/RiskBadge';
import { StudentGradesTab } from './StudentGradesTab';
import { StudentHistoryTab } from './StudentHistoryTab';
import { studentsGateway } from './students-gateway';

export function StudentProfilePage() {
  const { connectionRef = '', studentId = '' } = useParams();
  const query = useQuery({
    queryKey: ['portal', 'student', connectionRef, studentId],
    queryFn: () => studentsGateway.get(connectionRef, studentId),
  });

  return <main className="content-frame student-profile">
    <Link to="/alunos">← Alunos</Link>
    {query.isPending && <p>Carregando aluno…</p>}
    {query.isError && <p role="alert">Não foi possível carregar o perfil.</p>}
    {query.data && <>
      <header className="page-heading"><div><p className="eyebrow">ALUNO · {query.data.data.connectionRef}</p><h1>{query.data.data.name}</h1><p>{query.data.data.email ?? 'Email não informado'}</p></div><RiskBadge level={query.data.data.risk} /></header>
      <Card><CardHeader><CardTitle>Resumo</CardTitle></CardHeader><CardContent>
        <p>Último acesso: {query.data.data.lastAccessAt ? new Date(query.data.data.lastAccessAt).toLocaleString('pt-BR') : 'Nunca'}</p>
        <p>Acesso ao curso: {query.data.data.lastCourseAccessAt ? new Date(query.data.data.lastCourseAccessAt).toLocaleString('pt-BR') : 'Nunca'}</p>
        <p>Identidade: {query.data.data.connectionRef}:{query.data.data.studentId}</p>
        {query.data.data.riskFactors.map(factor => <p key={factor}>{factor}</p>)}
        {query.data.data.moodleUrl && <a href={query.data.data.moodleUrl} target="_blank" rel="noreferrer">Abrir no Moodle</a>}
      </CardContent></Card>
      <section aria-label="Matrículas e progresso" className="student-enrollments">
        {query.data.data.courses.length === 0 ? <p>Nenhuma matrícula encontrada.</p> : query.data.data.courses.map(course => <Card key={`${course.connectionRef}:${course.courseId}`}><CardHeader><CardTitle>{course.name}</CardTitle></CardHeader><CardContent><p>Matrícula: {course.enrollmentStatus}</p><p>Progresso: {course.progress != null ? `${course.progress}%` : 'Não informado'}</p><p>Origem: {course.connectionRef}</p></CardContent></Card>)}
      </section>
      <StudentGradesTab courses={query.data.data.courses} /><StudentHistoryTab student={query.data.data} />
    </>}
  </main>;
}
