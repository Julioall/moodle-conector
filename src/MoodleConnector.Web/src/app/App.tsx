import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { AppLayout } from '../components/layout/AppLayout';
import { AuthGate } from '../features/auth/AuthGate';
import { ConnectionsPage } from '../features/connections/ConnectionsPage';
function FoundationHome() { return <section className="foundation-card"><p className="eyebrow">MOODLE CONNECTOR</p><h1>Portal acadêmico</h1><p>O shell do Portal v2 está pronto para receber os módulos acadêmicos.</p></section>; }
export function App() { return <BrowserRouter><AuthGate><Routes><Route element={<AppLayout />}><Route path="/conexoes" element={<ConnectionsPage />} /><Route path="*" element={<FoundationHome />} /></Route></Routes></AuthGate></BrowserRouter>; }
