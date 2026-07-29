# Comprexy Metrics Dashboard — UI Implementation Review

**Date:** 2026-07-29
**Reviewer:** Plan-gated UI review specialist (adversarial stance)
**Build:** Passes (exit 0, EPERM is sandbox-only)
**Tests:** 341 tests, all passing

---

## Verdict: APPROVED WITH CONDITIONS

The implementation broadly matches the approved plan's component breakdown. The test suite is substantial (341 tests across 21 files) and all pass. However, several issues range from plan deviations to test quality concerns that should be addressed before considering this complete.

---

## 1. Plan Coverage

### 1.1 Component Completeness

| Component | Plan | Implementation | Status |
|---|---|---|---|
| TopBar | Yes | Yes | PASS |
| DashboardShell | Yes | Yes | PASS |
| DashboardSkeleton | Yes | Yes | PASS |
| HeroCard | Yes | Yes | PASS |
| MetricCard | Yes | Yes | PASS |
| AverageCompressionCard | Yes | Yes | PASS |
| OverheadCard | Yes | Yes | PASS |
| BudgetTriggersCard | Yes | Yes | PASS |
| WorkingMemoryCard | Yes | Yes | PASS |
| BarChart | Yes | Yes | PASS |
| ChartTooltip | Yes | Yes | PASS |
| ChartLegend | Yes | Yes | PASS |
| GhostBar | Yes | Yes | PASS |

**All 13 planned components are implemented.**

### 1.2 Plan Deviations

**[FINDING-1] DashboardShell max-width deviation**

Plan specifies `max-w-[1280px]`. Implementation uses `max-w-[1920px]`.

```tsx:14:apps/dashboard/src/components/layout/dashboard-shell.tsx
const DashboardShell: React.FC<DashboardShellProps> = ({ children }) => {
  return (
    <div className="flex h-screen w-full flex-col">
      <TopBar />
      <main className="overflow-auto p-6">
        <div className="mx-auto max-w-[1920px] space-y-6">
```

This is a 50% width increase from the plan. While wider layouts may be intentional for dashboard data density, this is a scope deviation that should be explicitly called out.

**[FINDING-2] BudgetTriggersCard and WorkingMemoryCard receive hardcoded values**

In `app/page.tsx`, both components are wired with static dummy values rather than actual API data:

```tsx:~:apps/dashboard/src/app/page.tsx
<WorkingMemoryCard maxWorkingMemoryVersion={null} />
<BudgetTriggersCard budgetTriggerCount={0} />
```

The plan likely intended these to display live data. Hardcoded `0` and `null` mean these cards will always show zero/empty until wired up.

---

## 2. Test Quality Assessment

### 2.1 Strong Tests

**TopBar (`top-bar.test.tsx`)** — Well-structured, 16 tests. Proper mock isolation with `vi.mock()` calls in module scope. Tests interaction (select change, theme toggle), state transitions (loading, health status), and conditional rendering. The combobox interaction test at line 117 is a good example of proper user-interaction testing.

**useTheme (`use-theme.test.tsx`)** — Thorough, 14 tests. Covers localStorage persistence, system preference fallback, stored-over-system-preference priority, class application/removal, and toggle behavior. The mock setup in `beforeEach` is clean.

**Utils (`utils.test.tsx`)** — Comprehensive, 50+ tests. Good edge case coverage: boundary values at 1000/1000000 for `formatCompactNumber`, negative numbers, zero, null/undefined fields for `transformTurnsToChartData`. The `transformTurnsToChartData` tests are particularly good — they construct realistic `ConversationTurnMetricDto` objects and verify computed fields.

**Dashboard Store (`dashboard-store.test.ts`)** — Clean, well-organized. Tests each setter individually plus a `resetFilters` integration test. The `resetStore` helper ensures test isolation.

### 2.2 Weak Tests

**[FINDING-3] Bar chart tests heavily mock recharts — potential false confidence**

The bar chart test file (`bar-chart.test.tsx`) mocks `recharts` entirely:

```tsx:51:apps/dashboard/src/__tests__/charts/bar-chart.test.tsx
vi.mock('recharts', () => ({
  BarChart: ({ children, data, ...props }: any) => (
    <div data-testid="recharts-bar-chart" {...props}>
      <div data-testid="recharts-data">{JSON.stringify(data)}</div>
      {children}
    </div>
  ),
  Bar: ({ name, dataKey, ...props }: any) => (
    <div
      data-testid={`recharts-bar-${dataKey}`}
      data-name={name}
      data-datakey={dataKey}
      {...props}
    />
  ),
  // ... more stubs
}));
```

19 tests for BarChart, but they mostly verify that the mock DOM elements exist. The tests assert on `data-testid` attributes of the mock components rather than actual recharts rendering behavior. This means:
- If the data transformation logic is wrong, the tests would still pass (they only check that transformed data has certain keys)
- If recharts props are wrong, the mock wouldn't catch it
- The tests verify the wrapper's structure, not the chart's visual correctness

This is not necessarily bad — shallow rendering with mocks is a valid strategy — but the test assertions should be stronger about verifying the data transformation is correct (e.g., checking specific data values in the transformed output).

**[FINDING-4] Tooltip test — CSS class assertions (brittle)**

The tooltip test file has 32+ tests, but many assert on CSS class names rather than behavior:

```tsx:~:apps/dashboard/src/__tests__/charts/chart-tooltip.test.tsx
expect(container.querySelector('.bg-gray-900')).toBeInTheDocument();
expect(container.querySelector('.text-white')).toBeInTheDocument();
```

CSS class assertions are brittle — they break when Tailwind reclasses or design changes. The tests should instead assert on the tooltip's content (what data it displays) and its show/hide behavior.

**[FINDING-5] DashboardShell tests are thin**

The DashboardShell test file has only 6 tests, all checking DOM structure (CSS classes, element existence). No tests for:
- Props passthrough behavior
- Edge cases (empty children, null children)
- Accessibility attributes

The test at line 46 checking for `.max-w-\\[1920px\\]` is a CSS class assertion that will need updating if the max-width is ever changed again.

**[FINDING-6] DashboardSkeleton tests check CSS selectors, not behavior**

The 8 skeleton tests all query by CSS class selectors (`.animate-pulse`, `.h-32.w-full`, `.grid-cols-1.gap-4`). These are structural assertions that verify the skeleton exists but don't test any meaningful behavior.

### 2.3 Missing Test Categories

**[FINDING-7] No integration tests between components**

There are no tests that render a parent component with its children to verify the integration works. For example:
- DashboardShell rendering TopBar + children
- BarChart rendering ChartLegend + ChartTooltip
- Page.tsx wiring all cards together

**[FINDING-8] No test for page.tsx data flow**

The entry point (`app/page.tsx`) wires all components together but receives no test coverage. Specifically:
- How data from API flows through to individual cards
- Whether the loading/error states are properly handled
- The conversation selection flow from TopBar to data fetching

---

## 3. Handoff vs. Reality

The handoff (`handoff.md`) states that layout, hooks, and charts were "not yet tested." This is **stale** — test files exist for all three categories:

| Category | Handoff Status | Actual |
|---|---|---|
| Layout (TopBar, DashboardShell, DashboardSkeleton) | "not yet tested" | 3 test files, 30+ tests |
| Hooks (useTheme, useConversationUrl) | "not yet tested" | 2 test files, 20+ tests |
| Charts (BarChart, ChartTooltip, ChartLegend, GhostBar) | "not yet tested" | 4 test files, 50+ tests |

The coverage gap list in the handoff may also be outdated since tests for these components exist.

---

## 4. Code Quality Observations

### 4.1 Positive

- Consistent component structure across all metric cards
- Clean mock setup in test files (module-scoped `vi.mock()`)
- Good use of TypeScript types throughout
- Store is well-structured with clear action separation
- Utility functions are pure and well-tested

### 4.2 Concerns

- **Hardcoded values in page.tsx**: BudgetTriggersCard and WorkingMemoryCard receive `0` and `null` respectively. These should be wired to actual data or explicitly marked as "placeholder" in the component.
- **No error boundary**: The dashboard has no error boundary to catch render errors in child components.
- **No responsive testing**: While the layout uses responsive Tailwind classes, there are no tests verifying responsive behavior.

---

## 5. Recommendations

### Must Fix (Blocking)

1. **Wire BudgetTriggersCard and WorkingMemoryCard to real data** — Either connect them to the API or explicitly mark them as not-yet-wired in the UI. Hardcoded `0` and `null` are misleading to users.

2. **Resolve max-width deviation** — Either revert to the plan's `1280px` or document the intentional change to `1920px` with a rationale.

### Should Fix (Pre-Merge)

3. **Add integration tests** — At minimum, test that DashboardShell renders TopBar correctly and that the page component wires data to all cards.

4. **Strengthen bar chart tests** — Add assertions about the actual data values passed to recharts, not just that the mock DOM exists.

5. **Reduce CSS class assertions** — Replace brittle class name checks with behavior-based assertions where possible, especially in tooltip and skeleton tests.

### Nice to Have

6. **Add error boundary tests** — Verify error states are handled gracefully.

7. **Add page.tsx tests** — Test the data flow from API response to component props.

8. **Document plan deviations** — The max-width change should be documented in the plan or a decision log.

---

## Summary Table

| Category | Score | Notes |
|---|---|---|
| Plan Fidelity | 8/10 | All components present; max-width and hardcoded data are deviations |
| Test Coverage | 7/10 | 341 tests is strong; gaps in integration and page wiring |
| Test Quality | 6/10 | Heavy mocking, brittle CSS assertions, thin skeleton tests |
| Code Quality | 8/10 | Clean structure, good types, missing error boundary |
| Build | 10/10 | Passes cleanly |
| **Overall** | **7.5/10** | **APPROVED WITH CONDITIONS** |
