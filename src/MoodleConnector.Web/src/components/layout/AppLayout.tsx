import { Outlet } from 'react-router-dom';
import { AppSidebar } from './AppSidebar';
import { TopBar } from './TopBar';
export function AppLayout() { return <div className="app-shell"><AppSidebar /><div className="app-main"><TopBar /><main><Outlet /></main></div></div>; }
