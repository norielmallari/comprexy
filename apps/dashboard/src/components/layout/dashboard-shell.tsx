/**
 * DashboardShell provides the main layout container with TopBar, login gate, and content area.
 */

'use client';

import type { ReactNode } from 'react';
import { useCallback, useEffect, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';

import { LoginGate } from '@/components/auth/login-gate';
import { TopBar } from '@/components/layout/top-bar';
import { Skeleton } from '@/components/ui/skeleton';
import { hydrateCostModelKeyFromSession } from '@/lib/store/dashboard-store';

interface DashboardShellProps {
  children: ReactNode;
}

/**
 * Main dashboard layout shell.
 *
 * Provides the TopBar, login gate, and a scrollable content area with consistent padding.
 * The content wrapper is a flex column so pages can grow a chart into leftover height.
 */
export function DashboardShell({ children }: DashboardShellProps) {
  const [loginOpen, setLoginOpen] = useState(false);
  const queryClient = useQueryClient();

  useEffect(() => {
    hydrateCostModelKeyFromSession();
  }, []);

  const handleAuthenticated = useCallback(() => {
    void queryClient.invalidateQueries();
  }, [queryClient]);

  const handleCleared = useCallback(() => {
    void queryClient.invalidateQueries();
  }, [queryClient]);

  return (
    <div className="flex h-screen w-full flex-col bg-background">
      <TopBar onRequestLogin={() => setLoginOpen(true)} />
      <LoginGate
        open={loginOpen}
        onOpenChange={setLoginOpen}
        onAuthenticated={handleAuthenticated}
        onCleared={handleCleared}
      />
      <main className="flex min-h-0 flex-1 flex-col overflow-auto p-3">
        <div className="mx-auto flex min-h-0 w-full max-w-[1920px] flex-1 flex-col">
          {children}
        </div>
      </main>
    </div>
  );
}

/**
 * Loading skeleton for the dashboard content area.
 */
export function DashboardSkeleton() {
  return (
    <div className="flex min-h-0 flex-1 flex-col gap-4">
      <Skeleton variant="rectangular" className="h-20 w-full shrink-0" />
      <div className="grid shrink-0 grid-cols-1 gap-2 md:grid-cols-2 lg:grid-cols-4">
        <Skeleton variant="rectangular" className="h-16" />
        <Skeleton variant="rectangular" className="h-16" />
        <Skeleton variant="rectangular" className="h-16" />
        <Skeleton variant="rectangular" className="h-16" />
      </div>
      <Skeleton variant="rectangular" className="min-h-0 w-full flex-1" />
    </div>
  );
}
