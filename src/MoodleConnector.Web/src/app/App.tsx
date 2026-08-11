import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { NotFound } from './NotFound';
import { AppLayout } from '../components/layout/AppLayout';
import { AuthGate } from '../features/auth/AuthGate';
import { ConnectionsPage } from '../features/connections/ConnectionsPage';
import { MyCoursesPage } from '../features/courses/MyCoursesPage';
import { CoursePanelPage } from '../features/courses/CoursePanelPage';
import { StudentsPage } from '../features/students/StudentsPage';
import { StudentProfilePage } from '../features/students/StudentProfilePage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { TasksPage } from '../features/tasks/TasksPage';
import { AgendaPage } from '../features/agenda/AgendaPage';
import { FollowupPage } from '../features/followup/FollowupPage';
import { MessagesPage } from '../features/messages/MessagesPage';
import { ReportsPage } from '../features/reports/ReportsPage';
import { SettingsPage } from '../features/settings/SettingsPage';
import { SchoolsPage } from '../features/schools/SchoolsPage';

export function App() {
  return <BrowserRouter basename=""><AuthGate><Routes><Route element={<AppLayout />}><Route path="/" element={<DashboardPage />} /><Route path="/conexoes" element={<ConnectionsPage />} /><Route path="/meus-cursos" element={<MyCoursesPage />} /><Route path="/escolas" element={<SchoolsPage />} /><Route path="/cursos/:connectionRef/:courseId" element={<CoursePanelPage />} /><Route path="/alunos" element={<StudentsPage />} /><Route path="/alunos/:connectionRef/:courseId/:studentId" element={<StudentProfilePage />} /><Route path="/tarefas" element={<TasksPage />} /><Route path="/agenda" element={<AgendaPage />} /><Route path="/followup" element={<FollowupPage />} /><Route path="/mensagens" element={<MessagesPage />} /><Route path="/relatorios" element={<ReportsPage />} /><Route path="/configuracoes" element={<SettingsPage />} /><Route path="*" element={<NotFound />} /></Route></Routes></AuthGate></BrowserRouter>;
}

