'use client';

import { Suspense } from 'react';

import { useConversations } from '@/lib/queries/use-conversations';
import { useMetricsSummary } from '@/lib/queries/use-metrics';
import { useTurnMetrics } from '@/lib/queries/use-turns';
import { useConversationUrl } from '@/hooks/use-conversation-url';
import { DashboardShell, DashboardSkeleton } from '@/components/layout';
import {
  ActualTokensCard,
  AverageCompressionCard,
  BaselineTokensCard,
  BestCompressionCard,
  HeroCard,
  OverheadCard,
  WeightedCompressionCard,
  WorkingMemoryCard,
} from '@/components/metrics';
import { BarChart } from '@/components/charts';
import {
  getAverageCompressionRatio,
  getBestCompressionRatio,
  getMaxWorkingMemoryVersion,
  transformTurnsToChartData,
} from '@/lib/utils';

function DashboardContent() {
  const { conversationId, isRestoringConversation } = useConversationUrl();

  const { isLoading: conversationsLoading } = useConversations();
  const { data: metrics, isLoading: metricsLoading } = useMetricsSummary(conversationId);
  const { data: turns, isLoading: turnsLoading } = useTurnMetrics(conversationId);

  const isLoading = conversationsLoading || metricsLoading || turnsLoading;
  const maxWorkingMemoryVersion = getMaxWorkingMemoryVersion(turns);
  const averageCompressionRatio = getAverageCompressionRatio(turns);
  const bestCompressionRatio = getBestCompressionRatio(turns);

  return (
    <DashboardShell>
      {!conversationId && !isRestoringConversation ? (
        <div
          className="flex min-h-[240px] flex-col items-center justify-center rounded-lg border border-dashed border-border bg-card/50 px-6 py-12 text-center"
          data-testid="metrics-empty-state"
        >
          <p className="text-lg font-medium text-foreground">No conversation selected</p>
          <p className="mt-2 max-w-md text-sm text-muted-foreground">
            Choose a conversation from the header selector to view compression metrics and turn
            charts.
          </p>
        </div>
      ) : isRestoringConversation || isLoading ? (
        <DashboardSkeleton />
      ) : (
        <div className="space-y-3">
          {/* Hero + Metric Cards Grid */}
          {metrics && (
            <div
              className="grid w-full grid-cols-1 gap-3 lg:grid-cols-2"
              data-testid="metrics-grid"
            >
              <div data-testid="metrics-top-left">
                <HeroCard tokensSaved={metrics.totalNetTokensSaved} />
              </div>
              <div
                className="grid grid-cols-1 gap-3 sm:grid-cols-2"
                data-testid="metrics-top-right"
              >
                <WeightedCompressionCard
                  weightedTokenSavingsRatio={metrics.averageTokenSavingsRatio}
                />
                <AverageCompressionCard
                  averageTokenSavingsRatio={averageCompressionRatio}
                />
              </div>
              <div
                className="grid grid-cols-1 gap-3 sm:grid-cols-2"
                data-testid="metrics-bottom-left"
              >
                <BaselineTokensCard
                  totalBaselineTokensEstimated={metrics.totalBaselineTokensEstimated}
                />
                <ActualTokensCard
                  totalActualTokensEstimated={metrics.totalActualTokensEstimated}
                />
              </div>
              <div
                className="grid grid-cols-1 gap-3 sm:grid-cols-3"
                data-testid="metrics-bottom-right"
              >
                <BestCompressionCard bestCompressionRatio={bestCompressionRatio} />
                <OverheadCard
                  totalCompressionOverheadTokens={metrics.totalCompressionOverheadTokens}
                  totalBaselineTokensEstimated={metrics.totalBaselineTokensEstimated}
                />
                <WorkingMemoryCard
                  maxWorkingMemoryVersion={maxWorkingMemoryVersion}
                />
              </div>
            </div>
          )}

          {/* Chart Section */}
          {turns && turns.length > 0 && (
            <div className="space-y-3">
              <BarChart
                data={transformTurnsToChartData(turns)}
              />
            </div>
          )}
        </div>
      )}
    </DashboardShell>
  );
}

export default function Home() {
  return (
    <Suspense fallback={<DashboardSkeleton />}>
      <DashboardContent />
    </Suspense>
  );
}
