import { useQuery } from '@tanstack/react-query';
import { FormEvent, useState } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/card';
import { reportsGateway } from './reports-gateway';

export function ReportsPage() {
  const operational = useQuery({ queryKey: ['portal', 'reports', 'operational'], queryFn: reportsGateway.operational });
  const audit = useQuery({ queryKey: ['portal', 'reports', 'audit'], queryFn: reportsGateway.audit });
  const [connectionRef, setConnectionRef] = useState('');
  const [courseId, setCourseId] = useState('');
  const [scope, setScope] = useState<{ connectionRef: string; courseId: string }>();
  const run = (event: FormEvent) => { event.preventDefault(); if (connectionRef.trim() && courseId.trim()) setScope({ connectionRef: connectionRef.trim(), courseId: courseId.trim() }); };
  const overview = useQuery({ queryKey: ['portal', 'reports', 'overview', scope], queryFn: () => reportsGateway.courseOverview(scope!.connectionRef, scope!.courseId), enabled: Boolean(scope) });
  const weekly = useQuery({ queryKey: ['portal', 'reports', 'weekly', scope], queryFn: () => reportsGateway.weekly(scope!.connectionRef, scope!.courseId), enabled: Boolean(scope) });
  const completion = useQuery({ queryKey: ['portal', 'reports', 'completion', scope], queryFn: () => reportsGateway.completion(scope!.connectionRef, scope!.courseId), enabled: Boolean(scope) });
  return <main className="content-frame">
    <header className="page-heading"><div><p className="eyebrow">GESTÃO</p><h1>Relatórios</h1><p>Indicadores determinísticos para acompanhamento manual.</p></div>{operational.data && <span className="freshness">Atualizado em {new Date(operational.data.meta.generatedAt).toLocaleString('pt-BR')}</span>}</header>
    {operational.isPending && <p>Carregando relatório…</p>}
    {operational.isError && <Card><CardContent><p role="alert">Não foi possível carregar o relatório.</p></CardContent></Card>}
    {operational.data && <section className="dashboard-stats" aria-label="Relatório operacional"><Card><CardHeader><CardTitle>Tarefas abertas</CardTitle></CardHeader><CardContent><strong>{operational.data.data.openTasks}</strong></CardContent></Card><Card><CardHeader><CardTitle>Tarefas concluídas</CardTitle></CardHeader><CardContent><strong>{operational.data.data.completedTasks}</strong></CardContent></Card><Card><CardHeader><CardTitle>Eventos próximos</CardTitle></CardHeader><CardContent><strong>{operational.data.data.upcomingEvents}</strong></CardContent></Card><Card><CardHeader><CardTitle>Follow-ups</CardTitle></CardHeader><CardContent><strong>{operational.data.data.followupsRecorded}</strong></CardContent></Card></section>}
    <Card><CardHeader><CardTitle>Relatórios acadêmicos sob demanda</CardTitle></CardHeader><CardContent><form onSubmit={run} className="form-grid"><label>ConnectionRef<input value={connectionRef} onChange={event => setConnectionRef(event.target.value)} placeholder="fieg" /></label><label>CourseId<input value={courseId} onChange={event => setCourseId(event.target.value)} placeholder="123" /></label><button type="submit">Gerar relatórios</button></form>{scope && <p className="freshness">Escopo: {scope.connectionRef}:{scope.courseId}</p>}{overview.data && <p>Visão geral: {overview.data.data.totalActiveStudents} alunos ativos, {overview.data.data.studentsInactiveDays} inativos.</p>}{weekly.data && <p>Semanal: {weekly.data.data.studentsWithAttention} em atenção, {weekly.data.data.studentsAtRisk} em risco.</p>}{completion.data && <p>Conclusão: {completion.data.data.likelyComplete} prováveis concluídos, {completion.data.data.pendingRecovery} em recuperação.</p>}{(overview.isError || weekly.isError || completion.isError) && <p role="alert">Não foi possível atualizar um dos relatórios acadêmicos.</p>}</CardContent></Card>
    {audit.data && <Card><CardHeader><CardTitle>Auditoria</CardTitle></CardHeader><CardContent><p>{audit.data.data.totalActions} ações, {audit.data.data.completedActions} concluídas, {audit.data.data.failedActions} falhas e {audit.data.data.confirmedActions} confirmações.</p></CardContent></Card>}
  </main>;
}
