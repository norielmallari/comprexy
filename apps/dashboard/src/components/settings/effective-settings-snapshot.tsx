/**
 * Conversation sticky effective-settings card — mode face, JSON on hover/focus.
 */

'use client';

import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { OptimizationModeValues } from '@/types/api';

interface EffectiveSettingsSnapshotProps {
  /** Sticky JSON from metrics summary, or null/undefined for N/A. */
  effectiveSettingsJson: string | null | undefined;
}

interface SnapshotSummary {
  hasSnapshot: boolean;
  label: string;
  prettyJson: string | null;
}

function normalizeOptimizationMode(value: unknown): string | null {
  if (value === OptimizationModeValues.Full || value === 'full' || value === 'Full') {
    return 'Full';
  }
  if (
    value === OptimizationModeValues.MonitorOnly ||
    value === 'monitorOnly' ||
    value === 'MonitorOnly'
  ) {
    return 'MonitorOnly';
  }
  return null;
}

/** Derive card face label + pretty JSON from sticky snapshot wire. */
export function summarizeEffectiveSettingsJson(
  effectiveSettingsJson: string | null | undefined,
): SnapshotSummary {
  if (typeof effectiveSettingsJson !== 'string' || effectiveSettingsJson.trim().length === 0) {
    return { hasSnapshot: false, label: 'N/A', prettyJson: null };
  }

  const raw = effectiveSettingsJson.trim();
  try {
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    const prettyJson = JSON.stringify(parsed, null, 2);
    if (parsed.passThrough === true) {
      return { hasSnapshot: true, label: 'PassThrough', prettyJson };
    }
    const mode = normalizeOptimizationMode(parsed.optimizationMode);
    return {
      hasSnapshot: true,
      label: mode ?? 'Snapshot',
      prettyJson,
    };
  } catch {
    return { hasSnapshot: true, label: 'Snapshot', prettyJson: raw };
  }
}

export function EffectiveSettingsSnapshot({
  effectiveSettingsJson,
}: EffectiveSettingsSnapshotProps) {
  const { hasSnapshot, label, prettyJson } = summarizeEffectiveSettingsJson(
    effectiveSettingsJson,
  );

  const ariaLabel =
    hasSnapshot
      ? `Conversation effective settings, ${label}`
      : 'Effective settings not available';
  const cardClassName =
    'h-full w-full rounded-lg border bg-white px-4 py-2.5 text-left dark:border-slate-700 dark:bg-slate-800';

  const card = (
    <button
      type="button"
      className={cardClassName}
      aria-label={ariaLabel}
      data-testid="effective-settings-snapshot"
    >
      <p className="mb-0.5 text-sm font-medium text-slate-500 dark:text-slate-400">
        Effective settings
      </p>
      {hasSnapshot ? (
        <p
          className="text-2xl font-semibold leading-tight text-slate-900 dark:text-slate-100"
          data-testid="effective-settings-mode"
        >
          {label}
        </p>
      ) : (
        <p
          className="text-2xl font-semibold leading-tight text-slate-500 dark:text-slate-400"
          data-testid="effective-settings-na"
        >
          N/A
        </p>
      )}
    </button>
  );

  if (!hasSnapshot || prettyJson === null) {
    return <div className="relative h-full">{card}</div>;
  }

  return (
    <div className="relative h-full">
      <Tooltip delayDuration={200}>
        <TooltipTrigger asChild className={cardClassName}>
          {card}
        </TooltipTrigger>
        <TooltipContent
          side="bottom"
          align="end"
          className="max-h-64 max-w-sm overflow-auto whitespace-pre-wrap break-all px-3 py-2 text-left font-mono text-xs leading-snug"
        >
          <pre className="m-0 whitespace-pre-wrap" data-testid="effective-settings-json">
            {prettyJson}
          </pre>
        </TooltipContent>
      </Tooltip>
    </div>
  );
}
