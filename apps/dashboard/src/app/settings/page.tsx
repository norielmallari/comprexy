'use client';

import { Suspense } from 'react';
import { useQueryClient } from '@tanstack/react-query';

import { DashboardShell, DashboardSkeleton } from '@/components/layout';
import { SettingsForm } from '@/components/settings/settings-form';
import {
  operatorSettingsKeys,
  useOperatorSettings,
} from '@/lib/queries/use-operator-settings';
import type { OperatorSettingsResponseDto } from '@/types/api';

function SettingsContent() {
  const queryClient = useQueryClient();
  const { data, error, isPending, isFetching, refetch } = useOperatorSettings();

  const showSkeleton = isPending || (isFetching && !data);

  return (
    <DashboardShell>
      <div className="mx-auto w-full max-w-3xl space-y-4" data-testid="settings-page">
        <div>
          <h2 className="text-xl font-semibold text-foreground">Operator settings</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Mutable allowlisted knobs stored in SQLite. Changes apply on the next request via
            options hot-reload. Auth secrets cannot be rotated from this UI.
          </p>
        </div>

        {showSkeleton && <DashboardSkeleton />}

        {error && !showSkeleton && (
          <div className="rounded-md border border-red-500/40 bg-red-50 px-4 py-3 text-sm text-red-900 dark:bg-red-950/30 dark:text-red-100" role="alert">
            {error.message ?? 'Failed to load settings'}
            <button
              type="button"
              className="ml-3 underline"
              onClick={() => void refetch()}
            >
              Retry
            </button>
          </div>
        )}

        {data && !showSkeleton && (
          <SettingsForm
            initial={data}
            onSaved={(next: OperatorSettingsResponseDto) => {
              queryClient.setQueryData(operatorSettingsKeys.detail(), next);
            }}
          />
        )}
      </div>
    </DashboardShell>
  );
}

export default function SettingsPage() {
  return (
    <Suspense fallback={<DashboardSkeleton />}>
      <SettingsContent />
    </Suspense>
  );
}
