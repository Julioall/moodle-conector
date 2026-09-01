import { lazy, Suspense } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';

import { NotFound } from './NotFound';
import { AppLayout } from '../components/layout/AppLayout';
import { Spinner } from '../components/ui/spinner';
import { AuthGate } from '../features/auth/AuthGate';

const MyCoursesPage = lazy(() => import('../features/courses/MyCoursesPage').then(({ MyCoursesPage }) => ({ default: MyCoursesPage })));
const CoursePanelPage = lazy(() => import('../features/courses/CoursePanelPage').then(({ CoursePanelPage }) => ({ default: CoursePanelPage })));
const StudentProfilePage = lazy(() => import('../features/students/StudentProfilePage').then(({ StudentProfilePage }) => ({ default: StudentProfilePage })));
const TasksPage = lazy(() => import('../features/tasks/TasksPage').then(({ TasksPage }) => ({ default: TasksPage })));
const AgendaPage = lazy(() => import('../features/agenda/AgendaPage').then(({ AgendaPage }) => ({ default: AgendaPage })));
const SettingsPage = lazy(() => import('../features/settings/SettingsPage').then(({ SettingsPage }) => ({ default: SettingsPage })));
const AdministrationPage = lazy(() => import('../features/settings/AdministrationPage').then(({ AdministrationPage }) => ({ default: AdministrationPage })));
const SchoolsPage = lazy(() => import('../features/schools/SchoolsPage').then(({ SchoolsPage }) => ({ default: SchoolsPage })));
const ReportHistoryPage = lazy(() => import('../features/reports/ReportHistoryPage').then(({ ReportHistoryPage }) => ({ default: ReportHistoryPage })));

export function App() {
  return (
    <BrowserRouter basename="">
      <AuthGate>
        <Suspense fallback={<div className="flex min-h-screen items-center justify-center"><Spinner className="h-8 w-8" /></div>}>
          <Routes>
            <Route element={<AppLayout />}>
              <Route path="/" element={<Navigate to="/meus-cursos" replace />} />
              {/* Keep the former standalone URL readable after connections moved into Settings. */}
              <Route path="/conexoes" element={<Navigate to="/configuracoes?tab=conexoes" replace />} />
              <Route path="/meus-cursos" element={<MyCoursesPage />} />
              {/* Preserve previously shared onboarding links after automatic course selection. */}
              <Route path="/selecionar-cursos" element={<Navigate to="/meus-cursos" replace />} />
              <Route path="/escolas" element={<SchoolsPage />} />
              <Route path="/cursos/:connectionRef/:courseId" element={<CoursePanelPage />} />
              <Route path="/cursos/:connectionRef/:courseId/alunos/:studentId" element={<StudentProfilePage />} />
              {/* Keep old profile URLs readable for bookmarks; the roster itself is now only a course tab. */}
              <Route path="/alunos/:connectionRef/:courseId/:studentId" element={<StudentProfilePage />} />
              <Route path="/tarefas" element={<TasksPage />} />
              <Route path="/agenda" element={<AgendaPage />} />
              <Route path="/followup" element={<Navigate to="/meus-cursos" replace />} />
              {/* The message API remains available for future campaigns. */}
              <Route path="/mensagens" element={<Navigate to="/" replace />} />
              <Route path="/pendencias" element={<Navigate to="/meus-cursos" replace />} />
              <Route path="/foruns" element={<Navigate to="/meus-cursos" replace />} />
              {/* Reports are generated from course and school selections; this page tracks their progress and files. */}
              <Route path="/relatorios" element={<ReportHistoryPage />} />
              <Route path="/configuracoes" element={<SettingsPage />} />
              <Route path="/administracao" element={<AdministrationPage />} />
              <Route path="*" element={<NotFound />} />
            </Route>
          </Routes>
        </Suspense>
      </AuthGate>
    </BrowserRouter>
  );
}
