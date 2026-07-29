'use client';

import { Suspense } from 'react';

import { useConversations } from '@/lib/queries/use-conversations';
import { useMetricsSummary } from '@/lib/queries/use-metrics';
import { useTurnMetrics } from '@/lib/queries/use-turns';
import { useDashboardStore } from '@/lib/store/dashboard-store';
import { useConversationUrl } from '@/hooks/use-conversation-url';
import { DashboardShell, TopBar, DashboardSkeleton } from '@/components/layout';
import {
  HeroCard,
  MetricCard,
  AverageCompressionCard,
  OverheadCard,
  BudgetTriggersCard,
  WorkingMemoryCard,
} from '@/components/metrics';
import { BarChart } from '@/components/charts';
import { Skeleton } from '@/components/ui/skeleton';
import { formatCompactNumber, formatPercentage, transformTurnsToChartData } from '@/lib/utils';
import { CHART_HEIGHT, CHART_WIDTH } from '@/lib/constants';

function DashboardContent() {
  const { conversationId, navigateToConversation } = useConversationUrl();
  const { theme } = useDashboardStore();

  const { data: conversations, isLoading: conversationsLoading } = useConversations();
  const { data: metrics, isLoading: metricsLoading } = useMetricsSummary(conversationId);
  const { data: turns, isLoading: turnsLoading } = useTurnMetrics(conversationId);

  const isLoading = conversationsLoading || metricsLoading || turnsLoading;

  return (
    <div className="min-h-screen bg-background text-foreground">
      <TopBar />

      <DashboardShell>
        {isLoading ? (
          <DashboardSkeleton />
        ) : (
          <div className="space-y-6">
            {/* Hero Section */}
            {metrics && (
              <HeroCard
                tokensSaved={metrics.totalNetTokensSaved}
                weightedCompressionRatio={metrics.averageTokenSavingsRatio}
              />
            )}

            {/* Metric Cards Grid */}
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
              {metrics && (
                <>
                  <MetricCard
                    title="Average Token Savings"
                    value={formatCompactNumber(metrics.averageTokenSavingsRatio)}
                    unit="tokens"
                    variant="default"
                  />
                  <AverageCompressionCard averageTokenSavingsRatio={metrics.averageTokenSavingsRatio} />
                  <OverheadCard
                    totalCompressionOverheadTokens={metrics.totalCompressionOverheadTokens}
                    totalBaselineTokensEstimated={metrics.totalBaselineTokensEstimated}
                  />
                  <BudgetTriggersCard budgetTriggerCount={0} />
                  <WorkingMemoryCard maxWorkingMemoryVersion={null} />
                </>
              )}
            </div>

            {/* Chart Section */}
            {turns && turns.length > 0 && (
              <div className="space-y-4">
                <BarChart
                  data={transformTurnsToChartData(turns)}
                />
              </div>
            )}
          </div>
        )}
      </DashboardShell>
    </div>
  );
}

export default function Home() {
  return (
    <Suspense fallback={<DashboardSkeleton />}>
      <DashboardContent />
    </Suspense>
  );
}
