# Comprexy Metrics Dashboard — Implementation Plan

## 1. Overview

This plan details the implementation of the Comprexy Metrics Dashboard, a single-page React application for visualizing compression performance data. The dashboard is conversation-centric, targeting desktop browsers with a hover-first interaction model.

---

## 2. Tech Stack (July 2026 Best Practices)

| Category | Technology | Rationale |
|----------|-----------|-----------|
| Framework | **Next.js 15+ (App Router)** | Server-first architecture, RSC, PPR, built-in optimization |
| Language | **TypeScript 5.x** | Strict mode, exact optional states, branded types |
| Styling | **Tailwind CSS v4** | Native CSS variables, zero-config, CSS-first |
| UI Primitives | **shadcn/ui** | Accessible, customizable, copy-paste components |
| State (Server) | **TanStack Query v5** | Conversation list, metrics, turns — all server state |
| State (Client) | **Zustand** | Theme, conversation selector — minimal client state |
| Forms | **React Hook Form + Zod** | If form elements needed (e.g., search input) |
| Charts | **recharts** | Composition-based, RSC-compatible, accessible |
| Testing | **Vitest + React Testing Library** | Fast, modern, consistent with Next.js ecosystem |
| Linting | **ESLint + Biome** | Biome for format/lint, ESLint for React/Next rules |
| Build | **Turbopack** | Built into Next.js, fast HMR |

---

## 3. Project Structure

```
apps/dashboard/
├── src/
│   ├── app/
│   │   ├── layout.tsx              # Root layout (fonts, theme, query provider)
│   │   ├── page.tsx                # Single-page dashboard entry
│   │   └── globals.css             # Tailwind directives, CSS variables
│   ├── components/
│   │   ├── layout/
│   │   │   ├── top-bar.tsx         # Conversation selector + theme toggle
│   │   │   └── dashboard-shell.tsx # Main container (1280px max-width)
│   │   ├── metrics/
│   │   │   ├── hero-card.tsx       # Tokens saved + weighted compression
│   │   │   ├── metric-card.tsx     # Reusable single-column card
│   │   │   ├── average-compression-card.tsx
│   │   │   ├── overhead-card.tsx
│   │   │   ├── budget-triggers-card.tsx
│   │   │   └── working-memory-card.tsx
│   │   ├── chart/
│   │   │   ├── bar-chart.tsx       # Main stacked bar chart
│   │   │   ├── chart-tooltip.tsx   # Hover tooltip
│   │   │   ├── chart-legend.tsx    # Legend for bar segments
│   │   │   └── ghost-bar.tsx       # Ghost bar component
│   │   └── ui/                     # shadcn/ui primitives
│   │       ├── select.tsx
│   │       ├── button.tsx
│   │       ├── badge.tsx
│   │       └── ...
│   ├── lib/
│   │   ├── api/
│   │   │   ├── client.ts           # Fetch wrapper for control-api
│   │   │   ├── conversations.ts    # Conversation list fetcher
│   │   │   ├── metrics.ts          # Metrics summary fetcher
│   │   │   └── turns.ts            # Per-turn metrics fetcher
│   │   ├── queries/
│   │   │   ├── use-conversations.ts    # Conversation list query
│   │   │   ├── use-metrics.ts          # Metrics summary query
│   │   │   └── use-turns.ts            # Per-turn metrics query
│   │   ├── store/
│   │   │   └── dashboard-store.ts  # Zustand store (theme, selectedConv)
│   │   ├── utils.ts                # formatNumbers, formatPercent, etc.
│   │   └── constants.ts            # Color tokens, chart config
│   ├── types/
│   │   ├── api.ts                  # API DTO type definitions
│   │   └── chart.ts                # Chart data structures
│   └── hooks/
│       ├── use-theme.ts            # Theme detection + persistence
│       └── use-conversation-url.ts # URL encoding of conversation ID
├── public/
│   └── favicon.ico
├── tailwind.config.ts              # Tailwind v4 config (minimal)
├── tsconfig.json
├── package.json
└── next.config.ts
```

---

## 4. API Endpoint Mapping

### 4.1 REST Endpoints (Control API on `:8130`)

| Dashboard Need | Endpoint | Response Field(s) | Notes |
|----------------|----------|-------------------|-------|
| Conversation list | `GET /v1/comprexy/conversations` | `ConversationMetricsListItemDto[]` | Sorted by `UpdatedAt` desc |
| Metrics summary | `GET /v1/comprexy/conversations/{id}/metrics` | `ConversationMetricsSummaryDto` | Whole-conversation aggregates |
| Per-turn data | `GET /v1/comprexy/conversations/{id}/metrics/turns` | `ConversationTurnMetricDto[]` | Per-turn breakdown for bar chart |

### 4.2 DTO Field Mapping

#### `ConversationMetricsSummaryDto` → Dashboard Metrics

| Dashboard Metric | DTO Field | Calculation |
|-----------------|-----------|-------------|
| **Tokens saved** | `TotalNetTokensSaved` | Direct field |
| **Weighted compression %** | Derived | `TotalNetTokensSaved / TotalBaselineTokensEstimated * 100` |
| **Average compression %** | `AverageTokenSavingsRatio` | Direct field (multiply by 100 for display) |
| **Overhead %** | Derived | `TotalCompressionOverheadTokens / TotalBaselineTokensEstimated * 100` |
| **Budget triggers** | Derived | Count turns where `SoftBudgetExceeded === true` from turns endpoint |
| **Working memory version** | Derived | Max `WorkingMemoryVersionUsed` from turns endpoint (null → v0) |

#### `ConversationTurnMetricDto` → Bar Chart Segments

The three stacked segments are disjoint slices of the prepared prompt and always sum to
`CompressedInputTokensEstimated`. The control-api derives them read-side (no extra columns).

| Chart Segment | DTO Field | Description |
|--------------|-----------|-------------|
| **System** | `SystemPromptTokensEstimated` | Conversation `SystemPrompt` estimated once; constant across turns |
| **History + tools** | `HistoryAndToolsTokensEstimated` | Remainder: `CompressedInputTokensEstimated - System - WM` |
| **Compressed WM** | `WorkingMemoryTokensEstimated` | `WorkingMemory.TokenCount` for `WorkingMemoryVersionUsed`; `0` when no WM exists yet |
| **Ghost bar** | `BaselineTotalTokensEstimated` | Uncompressed reference, drawn behind the stack on a hidden `xAxisId` |
| **Completion/output** | `ActualCompletionTokens` | Assistant output tokens (tooltip only, not stacked) |

There is no per-turn Overhead segment. Compression overhead is only tracked as a
conversation-level total (`TotalCompressionOverheadTokens`) on the summary, so plotting it
per turn would render as a constant zero.

### 4.3 MCP Telemetry Tools (Optional, for future enhancement)

| Tool | Purpose | Dashboard Relevance |
|------|---------|-------------------|
| `comprexy_get_compression_phase_breakdown` | Compression phases detail | Low priority |
| `comprexy_get_budget_events` | Budget/trim events | Could replace turn-counting for budget triggers |
| `comprexy_get_prompt_growth_timeline` | Prompt tokens per turn | Redundant with turns endpoint |
| `comprexy_get_final_turn_snapshot` | Final turn proof | Not needed |
| `comprexy_compare_conversations` | Side-by-side comparison | Could be a future feature |
| `comprexy_get_working_memory` | WM content | Not needed |
| `comprexy_get_recent_messages` | Recent messages | Not needed |
| `comprexy_get_open_tool_chains` | Tool chain status | Not needed |

**Decision:** Use only the REST endpoints. MCP tools are not needed because the REST endpoints already provide all required data.

---

## 5. Component Breakdown

### 5.1 Layout Components

#### `TopBar`
- **Props:** `conversations: ConversationListItem[]`, `selectedId: string`, `onSelect: (id: string) => void`, `theme: 'light' | 'dark'`, `onThemeToggle: () => void`
- **Elements:**
  - Left: `ConversationSelect` dropdown (280px wide)
  - Right: Theme toggle button (44×44px touch target)
- **State:** Controlled by parent (Zustand store)
- **Accessibility:** `aria-expanded`, `aria-controls`, visible focus ring (2px solid, 3px offset)

#### `DashboardShell`
- **Props:** `children: ReactNode`
- **Layout:** 1280px max-width, centered, 24px padding
- **CSS:** `margin: 0 auto; max-width: 1280px; padding: 0 24px;`

### 5.2 Metric Card Components

#### `HeroCard`
- **Props:** `tokensSaved: number`, `weightedCompression: number`
- **Layout:** 2-column grid, subtle blue background tint
- **Typography:** 48px monospaced numbers, font-weight 700
- **Colors:** Light mode `#f0f4f8`, dark mode `#1a2332`

#### `MetricCard` (reusable)
- **Props:** `title: string`, `value: string`, `unit: string`, `variant?: 'default' | 'compact'`
- **Layout:** Single-column card with title, large value, sub-unit label
- **Styling:** White background, gray borders (light mode) / dark background, subtle borders (dark mode)

#### Individual Cards
- `AverageCompressionCard` — displays `AverageTokenSavingsRatio * 100`
- `OverheadCard` — displays `TotalCompressionOverheadTokens / TotalBaselineTokensEstimated * 100`
- `BudgetTriggersCard` — counts turns with `SoftBudgetExceeded === true`
- `WorkingMemoryCard` — displays max `WorkingMemoryVersionUsed` as badge (e.g., "v3")

### 5.3 Chart Components

#### `BarChart`
- **Props:** `turns: TurnMetric[]`
- **Chart type:** Stacked horizontal bar chart (or vertical bars along x-axis)
- **Segments per turn (bottom to top), one `stackId="prompt"`:**
  1. System — light gray
  2. History + tools — slate
  3. Compressed WM — blue gradients by version (v0 lightest → v3 darkest)
- **Ghost bar:** Baseline column behind the stack, rendered on a hidden second `xAxisId` so
  recharts overlaps it instead of placing it side by side; declared first so it paints behind
- **Interaction:** Hover reveals tooltip with turn details
- **Scroll:** Horizontal scroll for many turns

#### `ChartTooltip`
- **Props:** `turn: TurnMetric`
- **Content:** Turn index, model, token counts, budget flags, WM version
- **Behavior:** Follows cursor on hover

#### `ChartLegend`
- **Content:** Segment color keys (system, history + tools, compressed WM, ghost)
- **Layout:** Horizontal legend below chart

### 5.4 UI Primitives (shadcn/ui)

- `Select` — Conversation selector dropdown
- `Button` — Theme toggle, interactive elements
- `Badge` — Working memory version badge
- `Tooltip` — Hover tooltips for chart segments
- `Skeleton` — Loading states

---

## 6. State Management Architecture

### 6.1 Server State (TanStack Query)

```typescript
// use-conversations.ts
export function useConversations() {
  return useQuery({
    queryKey: ['conversations'],
    queryFn: () => fetchConversations(),
    staleTime: 30_000, // 30s — conversations don't change rapidly
    select: (data) => data.sort((a, b) => b.updatedAt - a.updatedAt),
  });
}

// use-metrics.ts
export function useMetrics(conversationId: string) {
  return useQuery({
    queryKey: ['metrics', conversationId],
    queryFn: () => fetchMetrics(conversationId),
    enabled: !!conversationId,
  });
}

// use-turns.ts
export function useTurns(conversationId: string) {
  return useQuery({
    queryKey: ['turns', conversationId],
    queryFn: () => fetchTurns(conversationId),
    enabled: !!conversationId,
  });
}
```

### 6.2 Client State (Zustand)

```typescript
// dashboard-store.ts
interface DashboardState {
  selectedConversationId: string | null;
  theme: 'light' | 'dark';
  setSelectedConversationId: (id: string | null) => void;
  setTheme: (theme: 'light' | 'dark') => void;
}

export const useDashboardStore = create<DashboardState>((set) => ({
  selectedConversationId: null,
  theme: 'light',
  setSelectedConversationId: (id) => set({ selectedConversationId: id }),
  setTheme: (theme) => set({ theme }),
}));
```

### 6.3 URL State

- Conversation ID encoded in URL query parameter: `?conv=UUID`
- On page load, read from URL → set Zustand store → triggers TanStack Query
- On conversation change, update URL → triggers Zustand store → triggers TanStack Query
- No turn-level deep linking

---

## 7. Data Fetching Layer

### 7.1 API Client

```typescript
// lib/api/client.ts
const CONTROL_API_URL = process.env.NEXT_PUBLIC_CONTROL_API_URL || 'http://localhost:8130';

async function fetchJson<T>(endpoint: string): Promise<T> {
  const res = await fetch(`${CONTROL_API_URL}${endpoint}`);
  if (!res.ok) throw new Error(`API error: ${res.status} ${res.statusText}`);
  return res.json();
}
```

### 7.2 Fetch Functions

```typescript
// lib/api/conversations.ts
export async function fetchConversations(): Promise<ConversationListItemDto[]> {
  return fetchJson<ConversationListItemDto[]>('/v1/comprexy/conversations');
}

// lib/api/metrics.ts
export async function fetchMetrics(conversationId: string): Promise<ConversationMetricsSummaryDto> {
  return fetchJson<ConversationMetricsSummaryDto>(`/v1/comprexy/conversations/${conversationId}/metrics`);
}

// lib/api/turns.ts
export async function fetchTurns(conversationId: string): Promise<ConversationTurnMetricDto[]> {
  return fetchJson<ConversationTurnMetricDto[]>(`/v1/comprexy/conversations/${conversationId}/metrics/turns`);
}
```

### 7.3 Error Handling

- Network errors: Show "Unable to connect to Comprexy API" message
- 404: Show "Conversation not found" message
- 500: Show "Server error, please try again" with retry button
- Loading states: Skeleton loaders for all cards and chart

---

## 8. Chart Implementation

### 8.1 Library Selection: recharts

**Why recharts:**
- Composition-based API (fits React composition model)
- RSC-compatible (can be used in server components with `"use client"` boundary)
- Accessible (ARIA attributes, keyboard navigation)
- Well-maintained, large ecosystem
- Supports stacked bars, custom tooltips, legends
- Tree-shakeable (minimal bundle impact)

**Alternatives considered:**
- `visx`: Lower-level, more manual, larger bundle
- `d3`: Too low-level for this use case
- `nivo`: Opinionated, less flexible

### 8.2 Chart Data Transformation

The segments are read straight off the DTO — the dashboard must not re-derive them, because
only the server knows the system prompt text and the stored WM token counts.

```typescript
// Transform turn metrics into chart-ready data
function transformTurnsToChartData(turns: ConversationTurnMetricDto[]): ChartDataPoint[] {
  return turns.map((turn) => ({
    turnIndex: turn.turnIndex,
    model: turn.model,
    systemTokens: turn.systemPromptTokensEstimated,
    historyTokens: turn.historyAndToolsTokensEstimated,
    workingMemoryTokens: turn.workingMemoryTokensEstimated,
    preparedPromptTokens: turn.compressedInputTokensEstimated,
    baselineTokens: turn.rawInputTokensEstimated,
    workingMemoryVersion: turn.workingMemoryVersionUsed,
    netTokensSaved: turn.netTokensSaved,
    savingsRatio: turn.netTokenSavingsRatio,
    softBudgetExceeded: turn.softBudgetExceeded,
    hardBudgetExceeded: turn.hardBudgetExceeded,
  }));
}
```

### 8.3 WM Color Coding

```typescript
// WM version to color mapping
const WM_COLORS = {
  light: {
    0: '#e0e7ef', // lightest blue
    1: '#a8c4e0',
    2: '#6ba3d6',
    3: '#2d6bc4', // darkest blue
  },
  dark: {
    0: '#2a3a52', // lightest blue (dark mode)
    1: '#3d5a80',
    2: '#4a7ab5',
    3: '#5b8fd4', // darkest blue (dark mode)
  },
};
```

### 8.4 Chart Layout

```
┌─────────────────────────────────────────────────────────────┐
│  Y-axis: Token count (scales to max ghost bar height)       │
│                                                             │
│  ┌╌╌╌┐  ┌╌╌╌┐  ┌╌╌╌┐  ┌╌╌╌┐  ┌╌╌╌┐  ┌╌╌╌┐  ┌╌╌╌┐         │  ← Ghost = baseline
│  ╎   ╎  ╎   ╎  ╎   ╎  ╎   ╎  ╎   ╎  ╎   ╎  ╎   ╎         │    (dashed, behind,
│  ╎   ╎  ╎   ╎  ╎   ╎  ╎   ╎  ╎   ╎  ╎   ╎  ╎   ╎         │     grows every turn)
│  ╎   ╎  ╎   ╎  ╎   ╎  ╎   ╎  ╎   ╎  ╎   ╎  ╎   ╎         │
│  ╎   ╎  ╎   ╎  ╎   ╎  ╎┌─┐╎  ╎┌─┐╎  ╎┌─┐╎  ╎   ╎         │
│  ╎   ╎  ╎   ╎  ╎   ╎  ╎│W│╎  ╎│W│╎  ╎│W│╎  ╎   ╎         │  ← Compressed WM
│  ╎┌─┐╎  ╎┌─┐╎  ╎┌─┐╎  ╎├─┤╎  ╎├─┤╎  ╎├─┤╎  ╎┌─┐╎         │    (blue by version,
│  ╎│H│╎  ╎│H│╎  ╎│H│╎  ╎│H│╎  ╎│H│╎  ╎│H│╎  ╎│H│╎         │     absent until v1)
│  ╎│i│╎  ╎│i│╎  ╎│i│╎  ╎│i│╎  ╎│i│╎  ╎│i│╎  ╎│i│╎         │
│  ╎│s│╎  ╎│s│╎  ╎│s│╎  ╎│s│╎  ╎│s│╎  ╎│s│╎  ╎│s│╎         │  ← History + tools
│  ╎├─┤╎  ╎├─┤╎  ╎├─┤╎  ╎├─┤╎  ╎├─┤╎  ╎├─┤╎  ╎├─┤╎         │    (slate, resets on
│  ╎│S│╎  ╎│S│╎  ╎│S│╎  ╎│S│╎  ╎│S│╎  ╎│S│╎  ╎│S│╎         │     each WM rollover)
│  ╎│y│╎  ╎│y│╎  ╎│y│╎  ╎│y│╎  ╎│y│╎  ╎│y│╎  ╎│y│╎         │  ← System (constant)
│  └╌┴─┴╌┘└╌┴─┴╌┘└╌┴─┴╌┘└╌┴─┴╌┘└╌┴─┴╌┘└╌┴─┴╌┘└╌┴─┴╌┘        │
│     T1       T2       T3       T4       T5       T6         │
│                                                             │
│  X-axis: Turn index (1, 2, 3, ...)                          │
│                                                             │
│  Legend: [● System] [● History + tools] [● Compressed WM]   │
│          [○ Baseline (ghost)]                               │
└─────────────────────────────────────────────────────────────┘
```

---

## 9. Theming Strategy

### 9.1 Tailwind CSS v4 Configuration

```css
/* app/globals.css */
@import "tailwindcss";

@custom-variant dark (&:is(.dark *));

@theme {
  --color-bg-primary: #ffffff;
  --color-bg-secondary: #f8fafc;
  --color-bg-hero-light: #f0f4f8;
  --color-bg-hero-dark: #1a2332;
  --color-text-primary: #1a202c;
  --color-text-secondary: #718096;
  --color-border: #e2e8f0;
  --color-accent-blue: #2d6bc4;
  --color-accent-amber: #d69e2e;
  --color-chart-prompt: #a0aec0;
  --color-chart-system: #cbd5e0;
  --color-chart-wm-0-light: #e0e7ef;
  --color-chart-wm-1-light: #a8c4e0;
  --color-chart-wm-2-light: #6ba3d6;
  --color-chart-wm-3-light: #2d6bc4;
  --color-chart-wm-0-dark: #2a3a52;
  --color-chart-wm-1-dark: #3d5a80;
  --color-chart-wm-2-dark: #4a7ab5;
  --color-chart-wm-3-dark: #5b8fd4;
  --color-chart-overhead: #d69e2e;
  --color-chart-ghost: #cbd5e0;
}
```

### 9.2 Theme Detection + Persistence

```typescript
// hooks/use-theme.ts
export function useTheme() {
  const [theme, setTheme] = useState<'light' | 'dark'>('light');

  useEffect(() => {
    // Detect OS preference on mount
    const stored = localStorage.getItem('dashboard-theme');
    if (stored) {
      setTheme(stored as 'light' | 'dark');
    } else if (window.matchMedia('(prefers-color-scheme: dark)').matches) {
      setTheme('dark');
    }
  }, []);

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
    localStorage.setItem('dashboard-theme', theme);
  }, [theme]);

  return { theme, setTheme };
}
```

### 9.3 Dark Mode Strategy

- Use Tailwind's `dark:` variant modifier
- CSS variables for all themeable colors
- `prefers-color-scheme` as default, user override persisted in localStorage
- `document.documentElement.classList.toggle('dark', ...)` for class-based dark mode

---

## 10. Accessibility Implementation

### 10.1 WCAG 2.2 Compliance Checklist

| Criterion | Implementation |
|-----------|---------------|
| **1.1.1 Non-text Content** | Chart segments have `aria-label` with token counts |
| **1.4.1 Use of Color** | Chart segments use both color + pattern/label |
| **1.4.3 Contrast (Minimum)** | All text meets 4.5:1 ratio (light mode) / 3:1 (large text) |
| **1.4.11 Non-text Contrast** | UI components meet 3:1 contrast ratio |
| **2.1.1 Keyboard** | All interactive elements keyboard accessible |
| **2.4.3 Focus Order** | Logical tab order: selector → cards → chart |
| **2.4.7 Focus Visible** | 2px solid focus ring, 3px offset |
| **4.1.2 Name, Role, Value** | All components have proper ARIA attributes |

### 10.2 Chart Accessibility

- Each bar segment has `role="img"` with `aria-label`
- Keyboard navigation: arrow keys to move between turns
- Screen reader: "Turn {index}, {model}, {total} tokens, {savings} saved"
- Focus trap within chart area when focused

---

## 11. Build and Dev Tooling

### 11.1 Package.json Dependencies

```json
{
  "dependencies": {
    "next": "^15.1.0",
    "react": "^19.0.0",
    "react-dom": "^19.0.0",
    "recharts": "^2.15.0",
    "@tanstack/react-query": "^5.62.0",
    "zustand": "^5.0.0",
    "tailwindcss": "^4.0.0",
    "@tailwindcss/postcss": "^4.0.0",
    "clsx": "^2.1.0",
    "tailwind-merge": "^2.5.0",
    "class-variance-authority": "^0.7.0"
  },
  "devDependencies": {
    "typescript": "^5.7.0",
    "@types/react": "^19.0.0",
    "@types/react-dom": "^19.0.0",
    "@tanstack/react-query-devtools": "^5.62.0",
    "vitest": "^2.1.0",
    "@testing-library/react": "^16.1.0",
    "@next/eslint-plugin-next": "^15.1.0",
    "eslint": "^9.17.0",
    "@biomejs/biome": "^1.9.0"
  }
}
```

### 11.2 TypeScript Configuration

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "lib": ["ES2022", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "moduleResolution": "bundler",
    "strict": true,
    "exactOptionalPropertyTypes": true,
    "noUncheckedIndexedAccess": true,
    "noImplicitReturns": true,
    "noFallthroughCasesInSwitch": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "jsx": "preserve",
    "isolatedModules": true,
    "esModuleInterop": true,
    "resolveJsonModule": true,
    "skipLibCheck": true,
    "baseUrl": ".",
    "paths": {
      "@/*": ["src/*"]
    }
  },
  "include": ["next-env.d.ts", "**/*.ts", "**/*.tsx"],
  "exclude": ["node_modules"]
}
```

### 11.3 Development Workflow

```bash
# Install dependencies
npm install

# Start dev server (Turbopack)
npm run dev -- --turbopack

# Type check
npm run type-check

# Lint + format
npm run lint
npm run format

# Run tests
npm run test

# Build for production
npm run build

# Start production server
npm start
```

---

## 12. Implementation Phases

### Phase 1: Foundation (2-3 days)
- [ ] Scaffolding: Next.js app, Tailwind v4, TypeScript config
- [ ] Project structure: directories, types, API client
- [ ] Zustand store: theme, conversation selector
- [ ] URL encoding: conversation ID in query params
- [ ] Theme hook: OS detection + localStorage persistence
- [ ] Root layout: QueryProvider, font loading, CSS variables

### Phase 2: Data Layer (2-3 days)
- [ ] API client: fetch wrapper, error handling
- [ ] TypeScript types: API DTOs, chart data structures
- [ ] TanStack Query hooks: conversations, metrics, turns
- [ ] Data transformation: turn metrics → chart data
- [ ] Loading states: skeleton loaders for all components

### Phase 3: Layout + Cards (3-4 days)
- [ ] Top bar: conversation selector dropdown, theme toggle
- [ ] Dashboard shell: 1280px max-width container
- [ ] Hero card: tokens saved + weighted compression
- [ ] Metric cards: average compression, overhead, budget triggers, working memory
- [ ] Responsive adjustments: ensure 1440×900 minimum viewport looks good

### Phase 4: Chart (4-5 days)
- [ ] Bar chart: stacked segments, ghost bar
- [ ] Chart tooltip: hover interaction, turn details
- [ ] Chart legend: segment color keys
- [ ] WM color coding: version-based blue gradients
- [ ] Chart accessibility: keyboard nav, ARIA labels
- [ ] Horizontal scroll: for conversations with many turns

### Phase 5: Polish (2-3 days)
- [ ] Dark mode: all components tested in dark mode
- [ ] Accessibility audit: WCAG 2.2 compliance
- [ ] Error states: network errors, 404s, retry logic
- [ ] Empty states: no conversations, no turns
- [ ] Performance: memoization, virtualization if needed
- [ ] Testing: unit tests for utilities, chart components
- [ ] Build: production build verification

---

## 13. Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| **API field changes** | High | Type definitions from DTOs; adapter layer between API and UI |
| **Many turns (100+)** | Medium | Virtualization or pagination in chart; horizontal scroll |
| **CORS from browser** | Medium | Control-api CORS config; dev proxy in Next.js |
| **Chart performance** | Low-Medium | recharts is efficient; virtualization if needed |
| **Dark mode contrast** | Medium | Test all color combinations; use CSS variables |
| **Bundle size** | Low | Tree-shaking; recharts is ~50KB gzipped |

---

## 14. Future Enhancements (Out of Scope)

- [ ] MCP tool integration for additional telemetry
- [ ] Conversation comparison view (`comprexy_compare_conversations`)
- [ ] Compression phase breakdown visualization
- [ ] Prompt growth timeline chart
- [ ] Turn-level deep linking
- [ ] Export metrics as markdown (`comprexy_get_evidence_markdown`)
- [ ] Mobile-responsive layout
- [ ] Real-time updates (polling or WebSocket)
- [ ] System-wide aggregate view (not just per-conversation)

---

## 15. File Inventory

### Core Files (~30 files)

| File | Purpose | Lines (est.) |
|------|---------|-------------|
| `src/app/layout.tsx` | Root layout, providers | 40 |
| `src/app/page.tsx` | Dashboard entry point | 60 |
| `src/app/globals.css` | Tailwind, CSS variables | 80 |
| `src/components/layout/top-bar.tsx` | Top bar with selector + theme | 80 |
| `src/components/layout/dashboard-shell.tsx` | Main container | 30 |
| `src/components/metrics/hero-card.tsx` | Hero card | 60 |
| `src/components/metrics/metric-card.tsx` | Reusable card component | 50 |
| `src/components/metrics/average-compression-card.tsx` | Average compression card | 30 |
| `src/components/metrics/overhead-card.tsx` | Overhead card | 30 |
| `src/components/metrics/budget-triggers-card.tsx` | Budget triggers card | 30 |
| `src/components/metrics/working-memory-card.tsx` | Working memory card | 30 |
| `src/components/chart/bar-chart.tsx` | Main chart | 120 |
| `src/components/chart/chart-tooltip.tsx` | Hover tooltip | 60 |
| `src/components/chart/chart-legend.tsx` | Legend | 40 |
| `src/components/chart/ghost-bar.tsx` | Ghost bar | 30 |
| `src/lib/api/client.ts` | Fetch wrapper | 30 |
| `src/lib/api/conversations.ts` | Conversation fetcher | 20 |
| `src/lib/api/metrics.ts` | Metrics fetcher | 20 |
| `src/lib/api/turns.ts` | Turns fetcher | 20 |
| `src/lib/queries/use-conversations.ts` | Conversation query hook | 20 |
| `src/lib/queries/use-metrics.ts` | Metrics query hook | 20 |
| `src/lib/queries/use-turns.ts` | Turns query hook | 20 |
| `src/lib/store/dashboard-store.ts` | Zustand store | 40 |
| `src/lib/utils.ts` | Format helpers | 50 |
| `src/lib/constants.ts` | Color tokens, chart config | 40 |
| `src/types/api.ts` | API DTO types | 80 |
| `src/types/chart.ts` | Chart data types | 30 |
| `src/hooks/use-theme.ts` | Theme hook | 40 |
| `src/hooks/use-conversation-url.ts` | URL encoding hook | 30 |
| `src/components/ui/*.tsx` | shadcn/ui primitives | ~200 |

**Total estimated lines: ~1,500-2,000**

---

## 16. Key Design Decisions Summary

1. **No new backend endpoints** — UI adapts to existing REST endpoints
2. **REST over MCP** — REST endpoints provide all required data; MCP tools are redundant
3. **recharts over visx/d3** — Composition API fits React best practices; smaller learning curve
4. **Tailwind v4 over v3** — Native CSS variables, zero config, better dark mode support
5. **Zustand over Redux** — Minimal client state; Zustand is simpler and lighter
6. **TanStack Query over SWR** — Better TypeScript support, more features, active maintenance
7. **Next.js App Router over Pages** — Server-first, RSC, PPR, better performance
8. **Hover-first over click-first** — Matches dashboard spec; no modals, no drill-downs
9. **URL encoding over local storage for conversation ID** — Shareable URLs, browser back/forward
10. **Skeleton loaders over spinners** — Better UX, reduces perceived latency
