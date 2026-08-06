'use client';

import { Suspense, useCallback, useState } from 'react';

import {
  BenchmarkModeToggle,
  ComparisonPanel,
  CostModelPanel,
  RunStatusPanel,
  StartBenchmarkPanel,
  TelemetryPanel,
  type BenchmarkMode,
} from '@/components/benchmark';
import { DashboardShell, DashboardSkeleton } from '@/components/layout';
import { useBenchmarkRun } from '@/lib/api/benchmarks';
import { DEFAULT_COST_RATES } from '@/lib/benchmark-cost';
import type { BenchmarkCostRates } from '@/types/api';

function BenchmarkContent() {
  const [mode, setMode] = useState<BenchmarkMode>('telemetry');
  const [rates, setRates] = useState<BenchmarkCostRates>(DEFAULT_COST_RATES);
  const [telemetryConversationId, setTelemetryConversationId] = useState<string | null>(null);
  const [baselineId, setBaselineId] = useState<string | null>(null);
  const [compareId, setCompareId] = useState<string | null>(null);
  const [activeRunId, setActiveRunId] = useState<string | null>(null);

  const { data: activeRun, isLoading: runLoading } = useBenchmarkRun(
    activeRunId,
    Boolean(activeRunId),
  );

  const handleAutoFill = useCallback((baseline: string, compare: string) => {
    setBaselineId(baseline);
    setCompareId(compare);
    setMode('comparison');
  }, []);

  const modelKind = rates.modelKind;

  return (
    <DashboardShell>
      <div className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <BenchmarkModeToggle mode={mode} onChange={setMode} />
        </div>

        <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
          <div className="space-y-4 xl:col-span-1">
            <StartBenchmarkPanel
              rates={rates}
              modelKind={modelKind}
              activeRunId={activeRunId}
              onRunStarted={setActiveRunId}
              onRunCompleted={() => {
                /* status panel reflects terminal phase */
              }}
              onAutoFillIds={handleAutoFill}
            />
            <CostModelPanel rates={rates} onRatesChange={setRates} />
            <RunStatusPanel run={activeRun} isLoading={runLoading && Boolean(activeRunId)} />
          </div>

          <div className="xl:col-span-2">
            {mode === 'telemetry' ? (
              <TelemetryPanel
                conversationId={telemetryConversationId}
                onConversationChange={setTelemetryConversationId}
                rates={rates}
                modelKind={modelKind}
              />
            ) : (
              <ComparisonPanel
                baselineId={baselineId}
                compareId={compareId}
                onBaselineChange={setBaselineId}
                onCompareChange={setCompareId}
                rates={rates}
                modelKind={modelKind}
              />
            )}
          </div>
        </div>
      </div>
    </DashboardShell>
  );
}

export default function BenchmarkPage() {
  return (
    <Suspense fallback={<DashboardSkeleton />}>
      <BenchmarkContent />
    </Suspense>
  );
}
