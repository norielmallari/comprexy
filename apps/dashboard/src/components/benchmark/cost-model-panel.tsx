/**
 * Cost model panel with presets, local/USD toggle, and disclaimers.
 */

'use client';

import { Button } from '@/components/ui/button';
import {
  buildCostRates,
  COST_DISCLAIMER,
  COST_RATE_PRESETS,
  DEFAULT_COST_RATES,
  LOCAL_COST_DISCLAIMER,
} from '@/lib/benchmark-cost';
import type { BenchmarkCostRates, BenchmarkModelKind } from '@/types/api';

interface CostModelPanelProps {
  modelKind: BenchmarkModelKind;
  rates: BenchmarkCostRates;
  onModelKindChange: (kind: BenchmarkModelKind) => void;
  onRatesChange: (rates: BenchmarkCostRates) => void;
}

function RateInput({
  label,
  value,
  onChange,
  disabled,
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
  disabled?: boolean;
}) {
  return (
    <label className="flex flex-col gap-1 text-sm">
      <span className="text-slate-500">{label}</span>
      <input
        type="number"
        min={0}
        step={0.01}
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(parseFloat(e.target.value) || 0)}
        className="rounded border border-border bg-background px-2 py-1 text-sm disabled:opacity-50"
      />
    </label>
  );
}

export function CostModelPanel({
  modelKind,
  rates,
  onModelKindChange,
  onRatesChange,
}: CostModelPanelProps) {
  const usdDisabled = modelKind === 'local';

  const applyPreset = (presetId: string) => {
    const preset = COST_RATE_PRESETS.find((p) => p.id === presetId);
    if (preset) {
      onRatesChange(buildCostRates(preset.rates, modelKind));
    }
  };

  const updateRate = (field: keyof BenchmarkCostRates, value: number) => {
    if (field === 'modelKind') {
      return;
    }
    onRatesChange({ ...rates, [field]: value });
  };

  return (
    <section
      className="rounded-lg border bg-card p-4"
      aria-label="Cost model"
      data-testid="cost-model-panel"
    >
      <h3 className="text-base font-semibold">Cost model</h3>

      <div className="mt-3 flex flex-wrap gap-2">
        <Button
          type="button"
          size="sm"
          variant={modelKind === 'local' ? 'primary' : 'secondary'}
          onClick={() => {
            onModelKindChange('local');
            onRatesChange({ ...rates, modelKind: 'local' });
          }}
        >
          Local
        </Button>
        <Button
          type="button"
          size="sm"
          variant={modelKind === 'usd' ? 'primary' : 'secondary'}
          onClick={() => {
            onModelKindChange('usd');
            onRatesChange(buildCostRates(rates, 'usd'));
          }}
        >
          USD
        </Button>
      </div>

      <p className="mt-2 text-xs text-slate-500" data-testid="cost-model-disclaimer">
        {modelKind === 'local' ? LOCAL_COST_DISCLAIMER : COST_DISCLAIMER}
      </p>

      {modelKind === 'usd' && (
        <>
          <div className="mt-3 flex flex-wrap gap-2">
            {COST_RATE_PRESETS.map((preset) => (
              <Button
                key={preset.id}
                type="button"
                size="sm"
                variant="secondary"
                onClick={() => applyPreset(preset.id)}
              >
                {preset.label}
              </Button>
            ))}
            <Button
              type="button"
              size="sm"
              variant="ghost"
              onClick={() => onRatesChange(buildCostRates(DEFAULT_COST_RATES, 'usd'))}
            >
              Reset defaults
            </Button>
          </div>

          <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3">
            <RateInput
              label="Input $/1M"
              value={rates.inputUsdPer1M}
              onChange={(v) => updateRate('inputUsdPer1M', v)}
              disabled={usdDisabled}
            />
            <RateInput
              label="Output $/1M"
              value={rates.outputUsdPer1M}
              onChange={(v) => updateRate('outputUsdPer1M', v)}
              disabled={usdDisabled}
            />
            <RateInput
              label="Compression input $/1M"
              value={rates.compressionInputUsdPer1M}
              onChange={(v) => updateRate('compressionInputUsdPer1M', v)}
              disabled={usdDisabled}
            />
            <RateInput
              label="Compression output $/1M"
              value={rates.compressionOutputUsdPer1M}
              onChange={(v) => updateRate('compressionOutputUsdPer1M', v)}
              disabled={usdDisabled}
            />
            <RateInput
              label="Developer $/hr"
              value={rates.developerUsdPerHour}
              onChange={(v) => updateRate('developerUsdPerHour', v)}
              disabled={usdDisabled}
            />
            <RateInput
              label="Machine $/hr"
              value={rates.machineUsdPerHour}
              onChange={(v) => updateRate('machineUsdPerHour', v)}
              disabled={usdDisabled}
            />
          </div>
        </>
      )}
    </section>
  );
}
