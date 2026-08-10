import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { AppLayout } from '../components/layout/AppLayout';
import { AuthGate } from '../features/auth/AuthGate';
import { ConnectionsPage } from '../features/connections/ConnectionsPage';
import { MyCoursesPage } from '../features/courses/MyCoursesPage';
import { CoursePanelPage } from '../features/courses/CoursePanelPage';
import { StudentsPage } from '../features/students/StudentsPage';
import { StudentProfilePage } from '../features/students/StudentProfilePage';
function FoundationHome() { return <section className="foundation-card"><p className="eyebrow">MOODLE CONNECTOR</p><h1>Portal acadêmico</h1><p>O shell do Portal v2 está pronto para receber os módulos acadêmicos.</p></section>; }
export function App() { return <BrowserRouter><AuthGate><Routes><Route element={<AppLayout />}><Route path="/conexoes" element={<ConnectionsPage />} /><Route path="/meus-cursos" element={<MyCoursesPage />} /><Route path="/cursos/:connectionRef/:courseId" element={<CoursePanelPage />} /><Route path="/alunos" element={<StudentsPage />} /><Route path="/alunos/:connectionRef/:studentId" element={<StudentProfilePage />} /><Route path="*" element={<FoundationHome />} /></Route></Routes></AuthGate></BrowserRouter>; }
