# Unit Test Results — Dashboard Layout Redesign

## Summary

| Metric | Value |
|---|---|
| **Test Files** | 25 passed (25 total) |
| **Tests** | 367 passed (367 total) |
| **Duration** | ~1.3s |
| **Vitest Version** | v4.1.10 |

## New Test Files Created

| File | Tests | Status |
|---|---|---|
| `baseline-actual-card.test.tsx` | 8 | ✅ Pass |
| `compression-ratio-card.test.tsx` | 6 | ✅ Pass |
| `compression-health-card.test.tsx` | 7 | ✅ Pass |

## Updated Test Files

| File | Change | Status |
|---|---|---|
| `hero-card.test.tsx` | Removed `weightedCompressionRatio` assertions (12 → 6 tests) | ✅ Pass |

## Pre-existing Test Files (Unchanged)

All 22 other metric/layout/ui tests continue to pass without modification:

- `average-compression-card.test.tsx`
- `best-compression-card.test.tsx`
- `budget-triggers-card.test.tsx`
- `metric-card.test.tsx`
- `overhead-card.test.tsx`
- `working-memory-card.test.tsx`
- All chart, layout, UI, and utility tests

## Type Check

```
npx tsc --noemit
```

All new/modified files are type-clean. The only TS errors are pre-existing:
- `bar-chart.tsx` line 294: `"outsideBottom"` vs `"insideBottom"`
- `ghost-bar.test.tsx`, `dashboard-shell.test.tsx`, `top-bar.test.tsx`, `tooltip.test.tsx`, `utils.test.ts`

## Build

```
npm run build
```

Fails on the same pre-existing `bar-chart.tsx` type error. All new components compile correctly.

## Components Tested

### BaselineActualCard
- Renders baseline and actual values with formatting
- Computes and displays delta with "saved"/"over" label and percentage
- Handles null baseline, null actual, both null
- Renders all labels
- Has `role="region"` on root

### CompressionRatioCard
- Renders weighted and average compression values (same underlying data)
- Displays same value for both sub-panels
- Handles null ratio with placeholder
- Renders all labels
- Has `role="region"` on root

### CompressionHealthCard
- Renders best compression %, overhead %, and working memory badge
- Handles null best compression, null overhead values, null working memory
- Renders all labels
- Has `role="region"` on root

### HeroCard (updated)
- Renders only tokens saved (single metric, no grid)
- Handles null value with placeholder
- Renders label
- Has `role="region"` on root
