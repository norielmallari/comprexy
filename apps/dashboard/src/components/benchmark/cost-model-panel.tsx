/**
 * Cost model panel — catalog-backed rates + time-value knobs for benchmark runs.
 * Model selection lives in the shell CostModelPicker; this panel shows rates and
 * optional developer/machine $/hr for time-value deltas.
 */

'use client';

import { useEffect } from 'react';

import { COST_DISCLAIMER, LOCAL_COST_DISCLAIMER } from '@/components/cost/format-token-cost';
import { Button } from '@/components/ui/button';
import {
  catalogModelToBenchmarkRates,
  DEFAULT_TIME_VALUE_RATES,
} from '@/lib/benchmark-cost';
import { useCostModels } from '@/lib/queries/use-cost-models';
import { useDashboardStore } from '@/lib/store/dashboard-store';
import type { BenchmarkCostRates } from '@/types/api';

interface CostModelPanelProps {
  rates: BenchmarkCostRates;
  onRatesChange: (rates: BenchmarkCostRates) => void;
}

function RateInput({
  label,
  value,
  onChange,
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
}) {
  return (
    <label className="flex flex-col gap-1 text-sm">
      <span className="text-slate-500">{label}</span>
      <input
        type="number"
        min={0}
        step={0.01}
        value={value}
        onChange={(e) => onChange(parseFloat(e.target.value) || 0)}
        className="rounded border border-border bg-background px-2 py-1 text-sm disabled:opacity-50"
      />
    </label>
  );
}

export function CostModelPanel({ rates, onRatesChange }: CostModelPanelProps) {
  const selectedCostModelKey = useDashboardStore((s) => s.selectedCostModelKey);
  const { data: models, isLoading } = useCostModels();
  const selected = models?.find((m) => m.modelKey === selectedCostModelKey);

  useEffect(() => {
    if (!selected) {
      return;
    }
    const next = catalogModelToBenchmarkRates(selected, {
      developerUsdPerHour: rates.developerUsdPerHour,
      machineUsdPerHour: rates.machineUsdPerHour,
    });
    if (
      next.inputUsdPer1M !== rates.inputUsdPer1M ||
      next.outputUsdPer1M !== rates.outputUsdPer1M ||
      next.compressionInputUsdPer1M !== rates.compressionInputUsdPer1M ||
      next.compressionOutputUsdPer1M !== rates.compressionOutputUsdPer1M ||
      next.modelKind !== rates.modelKind
    ) {
      onRatesChange(next);
    }
    // Sync catalog model → rates; time-value fields are owned by this panel.
    // eslint-disable-next-line react-hooks/exhaustive-deps -- avoid loop on rates object identity
  }, [selected, selectedCostModelKey]);

  const isLocal = rates.modelKind === 'local';

  return (
    <section
      className="rounded-lg border bg-card p-4"
      aria-label="Cost model"
      data-testid="cost-model-panel"
    >
      <h3 className="text-base font-semibold">Cost model</h3>
      <p className="mt-1 text-sm text-muted-foreground">
        {isLoading
          ? 'Loading catalog…'
          : selected
            ? `${selected.displayLabel} (shell picker)`
            : 'Select a model in the top bar'}
      </p>

      <p className="mt-2 text-xs text-slate-500" data-testid="cost-model-disclaimer">
        {isLocal ? LOCAL_COST_DISCLAIMER : COST_DISCLAIMER}
      </p>

      {!isLocal && selected && (
        <dl className="mt-3 grid grid-cols-2 gap-2 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-slate-500">Input $/1M</dt>
            <dd className="font-medium">{Number(selected.inputUsdPer1M)}</dd>
          </div>
          <div>
            <dt className="text-slate-500">Output $/1M</dt>
            <dd className="font-medium">{Number(selected.outputUsdPer1M)}</dd>
          </div>
        </dl>
      )}

      <div className="mt-4 grid grid-cols-2 gap-3">
        <RateInput
          label="Developer $/hr"
          value={rates.developerUsdPerHour}
          onChange={(v) => onRatesChange({ ...rates, developerUsdPerHour: v })}
        />
        <RateInput
          label="Machine $/hr"
          value={rates.machineUsdPerHour}
          onChange={(v) => onRatesChange({ ...rates, machineUsdPerHour: v })}
        />
      </div>

      <div className="mt-3">
        <Button
          type="button"
          size="sm"
          variant="ghost"
          onClick={() =>
            onRatesChange({
              ...rates,
              developerUsdPerHour: DEFAULT_TIME_VALUE_RATES.developerUsdPerHour,
              machineUsdPerHour: DEFAULT_TIME_VALUE_RATES.machineUsdPerHour,
            })
          }
        >
          Reset time-value defaults
        </Button>
      </div>
    </section>
  );
}
