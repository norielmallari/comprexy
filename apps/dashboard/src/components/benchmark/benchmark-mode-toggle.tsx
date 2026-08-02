/**
 * Mode toggle for Telemetry vs Comparison views.
 */

'use client';

import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

export type BenchmarkMode = 'telemetry' | 'comparison';

interface BenchmarkModeToggleProps {
  mode: BenchmarkMode;
  onChange: (mode: BenchmarkMode) => void;
}

export function BenchmarkModeToggle({ mode, onChange }: BenchmarkModeToggleProps) {
  return (
    <div
      role="tablist"
      aria-label="Benchmark view mode"
      className="inline-flex rounded-md border border-border bg-card p-1"
      data-testid="benchmark-mode-toggle"
    >
      <Button
        type="button"
        role="tab"
        aria-selected={mode === 'telemetry'}
        variant={mode === 'telemetry' ? 'primary' : 'ghost'}
        size="sm"
        onClick={() => onChange('telemetry')}
        data-testid="benchmark-mode-telemetry"
      >
        Telemetry
      </Button>
      <Button
        type="button"
        role="tab"
        aria-selected={mode === 'comparison'}
        variant={mode === 'comparison' ? 'primary' : 'ghost'}
        size="sm"
        className={cn(mode === 'comparison' && 'ml-1')}
        onClick={() => onChange('comparison')}
        data-testid="benchmark-mode-comparison"
      >
        Comparison
      </Button>
    </div>
  );
}
