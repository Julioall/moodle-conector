import { Outlet } from 'react-router-dom';
import { AppSidebar } from './AppSidebar';
import { TopBar } from './TopBar';
import { SidebarProvider } from '@/components/ui/sidebar';
import { AppFooter } from '@/components/layout/AppFooter';
import { EditModeProvider } from './edit-mode';

export function AppLayout() {
  return (
    <EditModeProvider>
      <SidebarProvider>
        <div className="min-h-screen flex w-full bg-background">
          <AppSidebar />
          <div className="flex-1 flex flex-col min-w-0">
            <TopBar />
            <main className="flex-1 overflow-auto">
              <div className="container max-w-7xl px-4 py-6 md:px-6 lg:px-8">
                <Outlet />
              </div>
            </main>
            <AppFooter />
          </div>
        </div>
      </SidebarProvider>
    </EditModeProvider>
  );
}
