'use client';

import { Suspense } from 'react';

import { useConversations } from '@/lib/queries/use-conversations';
import { useMetricsSummary } from '@/lib/queries/use-metrics';
import { useTurnMetrics } from '@/lib/queries/use-turns';
import { useConversationUrl } from '@/hooks/use-conversation-url';
import { DashboardShell, DashboardSkeleton } from '@/components/layout';
import {
  HeroCard,
  AverageCompressionCard,
  BestCompressionCard,
  OverheadCard,
  WorkingMemoryCard,
} from '@/components/metrics';
import { BarChart } from '@/components/charts';
import {
  getBestCompressionRatio,
  getMaxWorkingMemoryVersion,
  transformTurnsToChartData,
} from '@/lib/utils';

function DashboardContent() {
  const { conversationId } = useConversationUrl();

  const { isLoading: conversationsLoading } = useConversations();
  const { data: metrics, isLoading: metricsLoading } = useMetricsSummary(conversationId);
  const { data: turns, isLoading: turnsLoading } = useTurnMetrics(conversationId);

  const isLoading = conversationsLoading || metricsLoading || turnsLoading;
  const maxWorkingMemoryVersion = getMaxWorkingMemoryVersion(turns);
  const bestCompressionRatio = getBestCompressionRatio(turns);

  return (
    <DashboardShell>
        {isLoading ? (
          <DashboardSkeleton />
        ) : (
          <div className="space-y-3">
            {/* Hero Section */}
            {metrics && (
              <HeroCard
                tokensSaved={metrics.totalNetTokensSaved}
                weightedCompressionRatio={metrics.averageTokenSavingsRatio}
              />
            )}

            {/* Metric Cards Grid */}
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
              {metrics && (
                <>
                  <BestCompressionCard bestCompressionRatio={bestCompressionRatio} />
                  <AverageCompressionCard averageTokenSavingsRatio={metrics.averageTokenSavingsRatio} />
                  <OverheadCard
                    totalCompressionOverheadTokens={metrics.totalCompressionOverheadTokens}
                    totalBaselineTokensEstimated={metrics.totalBaselineTokensEstimated}
                  />
                  <WorkingMemoryCard maxWorkingMemoryVersion={maxWorkingMemoryVersion} />
                </>
              )}
            </div>

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
