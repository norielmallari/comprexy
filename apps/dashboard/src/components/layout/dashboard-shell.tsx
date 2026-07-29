/**
 * DashboardShell provides the main layout container with TopBar and content area.
 */

'use client';

import type { ReactNode } from 'react';

import { TopBar } from '@/components/layout/top-bar';
import { Skeleton } from '@/components/ui/skeleton';

interface DashboardShellProps {
  children: ReactNode;
}

/**
 * Main dashboard layout shell.
 *
 * Provides the TopBar and a scrollable content area with consistent padding.
 */
export function DashboardShell({ children }: DashboardShellProps) {
  return (
    <div className="flex h-screen w-full flex-col bg-background">
      <TopBar />
      <main className="flex-1 overflow-auto p-6">
        <div className="mx-auto max-w-[1920px]">{children}</div>
      </main>
    </div>
  );
}

/**
 * Loading skeleton for the dashboard content area.
 */
export function DashboardSkeleton() {
  return (
    <div className="space-y-6">
      {/* Hero skeleton */}
      <Skeleton variant="rectangular" className="h-32 w-full" />

      {/* Metric cards skeleton */}
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-4">
        <Skeleton variant="rectangular" className="h-24" />
        <Skeleton variant="rectangular" className="h-24" />
        <Skeleton variant="rectangular" className="h-24" />
        <Skeleton variant="rectangular" className="h-24" />
      </div>

      {/* Charts skeleton */}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Skeleton variant="rectangular" className="h-80" />
        <Skeleton variant="rectangular" className="h-80" />
      </div>
    </div>
  );
}
