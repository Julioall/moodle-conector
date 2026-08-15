import { lazy, Suspense } from 'react';
import { BrowserRouter, Route, Routes } from 'react-router-dom';

import { NotFound } from './NotFound';
import { AppLayout } from '../components/layout/AppLayout';
import { Spinner } from '../components/ui/spinner';
import { AuthGate } from '../features/auth/AuthGate';

const ConnectionsPage = lazy(() => import('../features/connections/ConnectionsPage').then(({ ConnectionsPage }) => ({ default: ConnectionsPage })));
const MyCoursesPage = lazy(() => import('../features/courses/MyCoursesPage').then(({ MyCoursesPage }) => ({ default: MyCoursesPage })));
const CoursePanelPage = lazy(() => import('../features/courses/CoursePanelPage').then(({ CoursePanelPage }) => ({ default: CoursePanelPage })));
const StudentProfilePage = lazy(() => import('../features/students/StudentProfilePage').then(({ StudentProfilePage }) => ({ default: StudentProfilePage })));
const DashboardPage = lazy(() => import('../features/dashboard/DashboardPage').then(({ DashboardPage }) => ({ default: DashboardPage })));
const TasksPage = lazy(() => import('../features/tasks/TasksPage').then(({ TasksPage }) => ({ default: TasksPage })));
const AgendaPage = lazy(() => import('../features/agenda/AgendaPage').then(({ AgendaPage }) => ({ default: AgendaPage })));
const FollowupPage = lazy(() => import('../features/followup/FollowupPage').then(({ FollowupPage }) => ({ default: FollowupPage })));
const MessagesPage = lazy(() => import('../features/messages/MessagesPage').then(({ MessagesPage }) => ({ default: MessagesPage })));
const ReportsPage = lazy(() => import('../features/reports/ReportsPage').then(({ ReportsPage }) => ({ default: ReportsPage })));
const SettingsPage = lazy(() => import('../features/settings/SettingsPage').then(({ SettingsPage }) => ({ default: SettingsPage })));
const SchoolsPage = lazy(() => import('../features/schools/SchoolsPage').then(({ SchoolsPage }) => ({ default: SchoolsPage })));
const AutomationsPage = lazy(() => import('../features/automations/AutomationsPage').then(({ AutomationsPage }) => ({ default: AutomationsPage })));
const PendingCorrectionsPage = lazy(() => import('../features/corrections/PendingCorrectionsPage').then(({ PendingCorrectionsPage }) => ({ default: PendingCorrectionsPage })));
const CampaignsPage = lazy(() => import('../features/campaigns/CampaignsPage').then(({ CampaignsPage }) => ({ default: CampaignsPage })));
const ForumsPage = lazy(() => import('../features/forums/ForumsPage').then(({ ForumsPage }) => ({ default: ForumsPage })));

export function App() {
  return (
    <BrowserRouter basename="" future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <AuthGate>
        <Suspense fallback={<div className="flex min-h-screen items-center justify-center"><Spinner className="h-8 w-8" /></div>}>
          <Routes>
            <Route element={<AppLayout />}>
              <Route path="/" element={<DashboardPage />} />
              <Route path="/conexoes" element={<ConnectionsPage />} />
              <Route path="/meus-cursos" element={<MyCoursesPage />} />
              <Route path="/escolas" element={<SchoolsPage />} />
              <Route path="/cursos/:connectionRef/:courseId" element={<CoursePanelPage />} />
              <Route path="/cursos/:connectionRef/:courseId/alunos/:studentId" element={<StudentProfilePage />} />
              {/* Keep old profile URLs readable for bookmarks; the roster itself is now only a course tab. */}
              <Route path="/alunos/:connectionRef/:courseId/:studentId" element={<StudentProfilePage />} />
              <Route path="/tarefas" element={<TasksPage />} />
              <Route path="/agenda" element={<AgendaPage />} />
              <Route path="/followup" element={<FollowupPage />} />
              <Route path="/mensagens" element={<MessagesPage />} />
              <Route path="/pendencias" element={<PendingCorrectionsPage />} />
              <Route path="/campanhas" element={<CampaignsPage />} />
              <Route path="/foruns" element={<ForumsPage />} />
              <Route path="/automacoes" element={<AutomationsPage />} />
              <Route path="/relatorios" element={<ReportsPage />} />
              <Route path="/configuracoes" element={<SettingsPage />} />
              <Route path="*" element={<NotFound />} />
            </Route>
          </Routes>
        </Suspense>
      </AuthGate>
    </BrowserRouter>
  );
}
