/**
 * Budget Triggers card showing how many turns exceeded the soft budget.
 * Derived from MetricsSummary.TurnsSummary where SoftBudgetExceeded === true.
 */

import { Badge } from "@/components/ui/badge";
import { MetricCard } from "./metric-card";

interface BudgetTriggersCardProps {
  budgetTriggerCount: number | null;
}

export function BudgetTriggersCard({
  budgetTriggerCount,
}: BudgetTriggersCardProps) {
  const displayValue = budgetTriggerCount !== null ? String(budgetTriggerCount) : "—";

  return (
    <div className="space-y-3">
      <MetricCard
        title="Budget Triggers"
        value={displayValue}
        unit=""
        variant="compact"
      />
      {budgetTriggerCount !== null && budgetTriggerCount > 0 && (
        <Badge variant="warning">{budgetTriggerCount} turn{budgetTriggerCount > 1 ? "s" : ""} exceeded budget</Badge>
      )}
    </div>
  );
}
