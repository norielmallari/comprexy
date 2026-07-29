/**
 * GhostBar component — baseline comparison bar rendered behind chart bars.
 */

'use client';

import { Bar } from 'recharts';

export interface GhostBarProps {
  dataKey: string;
  baselineData: Record<string, unknown>[];
  fill?: string;
}

/**
 * Renders a ghost bar (baseline reference) behind the actual bars.
 * The ghost bar shows what the token count would be without compression.
 */
export function GhostBar({
  dataKey,
  baselineData,
  fill = '#cbd5e0',
}: GhostBarProps) {
  return (
    <Bar
      dataKey={dataKey}
      fill={fill}
      opacity={0.4}
      radius={[4, 4, 0, 0]}
    />
  );
}
