/**
 * Operator settings form — allowlisted Proxy / ContextPolicy / Metrics / ToolSchema knobs.
 * Secrets are never editable here.
 */

'use client';

import { FormEvent, useEffect, useState, type ReactNode } from 'react';

import { Button } from '@/components/ui/button';
import { putOperatorSettingsWithEtag } from '@/lib/api/settings';
import type {
  ApiError,
  OperatorMutableSettingsDto,
  OperatorSettingsResponseDto,
  OptimizationMode,
  PromptTokenBasis,
  ToolSchemaMode,
} from '@/types/api';
import {
  OptimizationModeValues,
  PromptTokenBasisValues,
  ToolSchemaModeValues,
} from '@/types/api';

interface SettingsFormProps {
  initial: OperatorSettingsResponseDto;
  onSaved: (next: OperatorSettingsResponseDto) => void;
}

function FieldLabel({ htmlFor, children }: { htmlFor: string; children: ReactNode }) {
  return (
    <label htmlFor={htmlFor} className="block text-sm font-medium text-foreground">
      {children}
    </label>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="rounded-lg border border-border bg-card p-4" aria-label={title}>
      <h3 className="text-base font-semibold text-foreground">{title}</h3>
      <div className="mt-3 space-y-3">{children}</div>
    </section>
  );
}

export function SettingsForm({ initial, onSaved }: SettingsFormProps) {
  const [revision, setRevision] = useState(initial.revision);
  const [passThrough, setPassThrough] = useState(initial.settings.proxy?.passThrough ?? false);
  const [optimizationMode, setOptimizationMode] = useState<OptimizationMode>(
    initial.settings.proxy?.optimizationMode ?? OptimizationModeValues.Full,
  );
  const [stripReasoning, setStripReasoning] = useState(
    initial.settings.proxy?.stripReasoningContent ?? false,
  );
  const [softLimitTokens, setSoftLimitTokens] = useState(
    initial.settings.contextPolicy?.softLimitTokens ?? 0,
  );
  const [minTurns, setMinTurns] = useState(
    initial.settings.contextPolicy?.minTurnsBetweenGenerations ?? 0,
  );
  const [retainCount, setRetainCount] = useState(
    initial.settings.contextPolicy?.compressionRetainMessageCount ?? 0,
  );
  const [dedupeFailedEdits, setDedupeFailedEdits] = useState(
    initial.settings.contextPolicy?.dedupeDuplicateFailedEdits ?? false,
  );
  const [cacheEnabled, setCacheEnabled] = useState(
    initial.settings.cacheAlignment?.enabled ?? false,
  );
  const [metricsEnabled, setMetricsEnabled] = useState(
    initial.settings.metrics?.enabled ?? true,
  );
  const [promptTokenBasis, setPromptTokenBasis] = useState<PromptTokenBasis>(
    initial.settings.metrics?.promptTokenBasis ?? PromptTokenBasisValues.ProviderActual,
  );
  const [toolSchemaMode, setToolSchemaMode] = useState<ToolSchemaMode>(
    initial.settings.toolSchema?.mode ?? ToolSchemaModeValues.Virtual,
  );
  const [excludeTools, setExcludeTools] = useState(
    (initial.settings.toolSchema?.excludeFromModelTools ?? []).join(', '),
  );
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    setRevision(initial.revision);
    setPassThrough(initial.settings.proxy?.passThrough ?? false);
    setOptimizationMode(
      initial.settings.proxy?.optimizationMode ?? OptimizationModeValues.Full,
    );
    setStripReasoning(initial.settings.proxy?.stripReasoningContent ?? false);
    setSoftLimitTokens(initial.settings.contextPolicy?.softLimitTokens ?? 0);
    setMinTurns(initial.settings.contextPolicy?.minTurnsBetweenGenerations ?? 0);
    setRetainCount(initial.settings.contextPolicy?.compressionRetainMessageCount ?? 0);
    setDedupeFailedEdits(initial.settings.contextPolicy?.dedupeDuplicateFailedEdits ?? false);
    setCacheEnabled(initial.settings.cacheAlignment?.enabled ?? false);
    setMetricsEnabled(initial.settings.metrics?.enabled ?? true);
    setPromptTokenBasis(
      initial.settings.metrics?.promptTokenBasis ?? PromptTokenBasisValues.ProviderActual,
    );
    setToolSchemaMode(initial.settings.toolSchema?.mode ?? ToolSchemaModeValues.Virtual);
    setExcludeTools((initial.settings.toolSchema?.excludeFromModelTools ?? []).join(', '));
  }, [initial]);

  const buildSettings = (): OperatorMutableSettingsDto => {
    const excludeList = excludeTools
      .split(',')
      .map((s) => s.trim())
      .filter((s) => s.length > 0);

    return {
      proxy: {
        passThrough,
        optimizationMode,
        stripReasoningContent: stripReasoning,
      },
      contextPolicy: {
        softLimitTokens: softLimitTokens > 0 ? softLimitTokens : null,
        minTurnsBetweenGenerations: minTurns > 0 ? minTurns : null,
        compressionRetainMessageCount: retainCount > 0 ? retainCount : null,
        dedupeDuplicateFailedEdits: dedupeFailedEdits,
      },
      cacheAlignment: {
        enabled: cacheEnabled,
      },
      metrics: {
        enabled: metricsEnabled,
        promptTokenBasis,
      },
      toolSchema: {
        mode: toolSchemaMode,
        excludeFromModelTools: excludeList.length > 0 ? excludeList : null,
      },
    };
  };

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setStatus('saving');
    setErrorMessage(null);
    try {
      const next = await putOperatorSettingsWithEtag(revision, buildSettings());
      setRevision(next.revision);
      setStatus('saved');
      onSaved(next);
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr.statusCode === 409) {
        setErrorMessage(
          `Revision conflict (409). Current revision is ${apiErr.currentRevision ?? 'unknown'}. Reload and retry.`,
        );
      } else {
        setErrorMessage(apiErr.message ?? 'Save failed');
      }
      setStatus('error');
    }
  };

  return (
    <form className="space-y-4" onSubmit={handleSubmit} data-testid="settings-form">
      {passThrough && (
        <div
          className="rounded-md border border-amber-500/40 bg-amber-50 px-4 py-3 text-sm text-amber-950 dark:bg-amber-950/30 dark:text-amber-100"
          role="status"
          data-testid="passthrough-wins-banner"
        >
          <strong>PassThrough wins.</strong> When <code className="text-xs">Proxy:PassThrough</code>{' '}
          is true, optimizations stay off and metrics are never recorded — including when
          OptimizationMode is MonitorOnly.
        </div>
      )}

      <p className="text-xs text-muted-foreground">
        Revision {revision}
        {initial.updatedAt ? ` · Updated ${new Date(initial.updatedAt).toLocaleString()}` : ''}
        . Secrets and auth keys are env/file only — not editable here.
      </p>

      <Section title="Proxy">
        <div className="flex flex-wrap gap-4">
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={passThrough}
              onChange={(e) => setPassThrough(e.target.checked)}
            />
            PassThrough
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={stripReasoning}
              onChange={(e) => setStripReasoning(e.target.checked)}
            />
            Strip reasoning content
          </label>
        </div>
        <div>
          <FieldLabel htmlFor="optimization-mode">Optimization mode</FieldLabel>
          <select
            id="optimization-mode"
            className="mt-1 w-full max-w-xs rounded-md border border-border bg-background px-3 py-2 text-sm"
            value={optimizationMode}
            onChange={(e) => setOptimizationMode(Number(e.target.value) as OptimizationMode)}
          >
            <option value={OptimizationModeValues.Full}>Full</option>
            <option value={OptimizationModeValues.MonitorOnly}>MonitorOnly</option>
          </select>
        </div>
      </Section>

      <Section title="Context policy">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          <div>
            <FieldLabel htmlFor="soft-limit">Soft limit tokens</FieldLabel>
            <input
              id="soft-limit"
              type="number"
              min={0}
              className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
              value={softLimitTokens}
              onChange={(e) => setSoftLimitTokens(parseInt(e.target.value, 10) || 0)}
            />
          </div>
          <div>
            <FieldLabel htmlFor="min-turns">Min turns between generations</FieldLabel>
            <input
              id="min-turns"
              type="number"
              min={0}
              className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
              value={minTurns}
              onChange={(e) => setMinTurns(parseInt(e.target.value, 10) || 0)}
            />
          </div>
          <div>
            <FieldLabel htmlFor="retain-count">Compression retain message count</FieldLabel>
            <input
              id="retain-count"
              type="number"
              min={0}
              className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
              value={retainCount}
              onChange={(e) => setRetainCount(parseInt(e.target.value, 10) || 0)}
            />
          </div>
        </div>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={dedupeFailedEdits}
            onChange={(e) => setDedupeFailedEdits(e.target.checked)}
          />
          Dedupe duplicate failed edits
        </label>
      </Section>

      <Section title="Cache alignment">
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={cacheEnabled}
            onChange={(e) => setCacheEnabled(e.target.checked)}
          />
          Enabled
        </label>
      </Section>

      <Section title="Metrics">
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={metricsEnabled}
            onChange={(e) => setMetricsEnabled(e.target.checked)}
          />
          Metrics enabled
        </label>
        <div>
          <FieldLabel htmlFor="prompt-token-basis">Prompt token basis</FieldLabel>
          <select
            id="prompt-token-basis"
            className="mt-1 w-full max-w-xs rounded-md border border-border bg-background px-3 py-2 text-sm"
            value={promptTokenBasis}
            onChange={(e) => setPromptTokenBasis(Number(e.target.value) as PromptTokenBasis)}
          >
            <option value={PromptTokenBasisValues.Estimated}>Estimated</option>
            <option value={PromptTokenBasisValues.ProviderActual}>ProviderActual</option>
          </select>
        </div>
      </Section>

      <Section title="Tool schema">
        <div>
          <FieldLabel htmlFor="tool-schema-mode">Mode</FieldLabel>
          <select
            id="tool-schema-mode"
            className="mt-1 w-full max-w-xs rounded-md border border-border bg-background px-3 py-2 text-sm"
            value={toolSchemaMode}
            onChange={(e) => setToolSchemaMode(Number(e.target.value) as ToolSchemaMode)}
          >
            <option value={ToolSchemaModeValues.Off}>Off</option>
            <option value={ToolSchemaModeValues.Virtual}>Virtual</option>
          </select>
        </div>
        <div>
          <FieldLabel htmlFor="exclude-tools">Exclude from model tools (comma-separated)</FieldLabel>
          <input
            id="exclude-tools"
            type="text"
            className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
            value={excludeTools}
            onChange={(e) => setExcludeTools(e.target.value)}
            placeholder="tool_a, tool_b"
          />
        </div>
      </Section>

      {errorMessage && (
        <p className="text-sm text-red-600 dark:text-red-400" role="alert" data-testid="settings-error">
          {errorMessage}
        </p>
      )}
      {status === 'saved' && (
        <p className="text-sm text-green-700 dark:text-green-400" role="status">
          Settings saved. Proxy hosts pick up changes on the next request.
        </p>
      )}

      <Button type="submit" disabled={status === 'saving'}>
        {status === 'saving' ? 'Saving…' : 'Save settings'}
      </Button>
    </form>
  );
}
