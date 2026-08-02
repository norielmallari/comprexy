/**
 * Footnotes for overhead, hop-count deltas, and comparison caveats.
 */

'use client';

interface BenchmarkCaveatsProps {
  caveats: string[];
}

export function BenchmarkCaveats({ caveats }: BenchmarkCaveatsProps) {
  if (caveats.length === 0) {
    return (
      <p className="text-xs text-slate-500" data-testid="benchmark-caveats">
        Compression overhead is counted once in totals — not shown as chart bars.
      </p>
    );
  }

  return (
    <div className="rounded-md border border-amber-200 bg-amber-50 p-3 dark:border-amber-800 dark:bg-amber-950/30">
      <p className="text-sm font-medium text-amber-800 dark:text-amber-200">Caveats</p>
      <ul className="mt-1 list-inside list-disc text-xs text-amber-700 dark:text-amber-300" data-testid="benchmark-caveats">
        {caveats.map((caveat) => (
          <li key={caveat}>{caveat}</li>
        ))}
        <li>Compression overhead is counted once in totals — not shown as chart bars.</li>
      </ul>
    </div>
  );
}
