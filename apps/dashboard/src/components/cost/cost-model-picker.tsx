/**
 * Shell-level cost model picker — selects a catalog model for `$` overlays.
 */

'use client';

import { useEffect } from 'react';

import { Select } from '@/components/ui/select';
import { COST_DISCLAIMER, isZeroRateModel } from '@/components/cost/format-token-cost';
import { useCostModels } from '@/lib/queries/use-cost-models';
import { useDashboardStore } from '@/lib/store/dashboard-store';

export function CostModelPicker() {
  const { data: models, isLoading, isError } = useCostModels();
  const selectedCostModelKey = useDashboardStore((s) => s.selectedCostModelKey);
  const setSelectedCostModelKey = useDashboardStore((s) => s.setSelectedCostModelKey);

  useEffect(() => {
    if (!models || models.length === 0) {
      return;
    }
    const exists = models.some((m) => m.modelKey === selectedCostModelKey);
    if (!exists) {
      const local = models.find((m) => m.modelKey === 'local') ?? models[0];
      setSelectedCostModelKey(local.modelKey);
    }
  }, [models, selectedCostModelKey, setSelectedCostModelKey]);

  const selected = models?.find((m) => m.modelKey === selectedCostModelKey);
  const showDisclaimer = selected && !isZeroRateModel(selected);

  return (
    <div className="flex flex-col items-end gap-0.5" data-testid="cost-model-picker">
      <div className="flex items-center gap-2">
        <span className="text-xs text-muted-foreground">Cost model</span>
        <Select
          aria-label="Cost model"
          options={
            isLoading
              ? [{ value: selectedCostModelKey, label: 'Loading…' }]
              : isError || !models
                ? [{ value: selectedCostModelKey, label: 'Unavailable' }]
                : models.map((m) => ({
                    value: m.modelKey,
                    label: m.displayLabel,
                  }))
          }
          value={selectedCostModelKey}
          onChange={setSelectedCostModelKey}
          className="w-44"
          disabled={isLoading || isError || !models?.length}
        />
      </div>
      {showDisclaimer && (
        <p className="whitespace-nowrap text-right text-[10px] leading-tight text-muted-foreground">
          {COST_DISCLAIMER}
        </p>
      )}
    </div>
  );
}
