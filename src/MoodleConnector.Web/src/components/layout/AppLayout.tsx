import { Outlet } from 'react-router-dom';
import { AppSidebar } from './AppSidebar';
import { TopBar } from './TopBar';
import { SidebarProvider } from '@/components/ui/sidebar';
import { AppFooter } from '@/components/layout/AppFooter';

export function AppLayout() {
  return (
    <SidebarProvider>
      <div className="min-h-screen flex w-full bg-background">
        <AppSidebar />
        <div className="flex-1 flex flex-col min-w-0">
          <TopBar />
          <div className="flex-1 overflow-auto">
            <div className="container py-6 px-4 md:px-6 lg:px-8 max-w-7xl">
              <Outlet />
            </div>
          </div>
          <AppFooter />
        </div>
      </div>
    </SidebarProvider>
  );
}
