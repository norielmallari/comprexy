/**
 * Zustand store for dashboard client state.
 *
 * Manages conversation selection, theme, and other UI state.
 */

import { create } from 'zustand';

// ---------------------------------------------------------------------------
// Store State
// ---------------------------------------------------------------------------

interface DashboardState {
  /** Currently selected conversation ID (null = none) */
  selectedConversationId: string | null;

  /** Currently selected conversation name for display */
  selectedConversationName: string | null;

  /** Theme: 'light' | 'dark' */
  theme: 'light' | 'dark';

  /** Selected working memory version filter (null = all) */
  selectedWmVersion: number | null;

  /** Selected compression overhead filter (null = all) */
  selectedOverhead: number | null;

  /** Selected token savings filter (null = all) */
  selectedSavings: number | null;

  /** Selected sort field */
  selectedSortField: 'updatedAt' | 'totalTurns' | 'totalNetTokensSaved';

  /** Selected sort direction */
  selectedSortDirection: 'asc' | 'desc';

  /** Current page for conversation list pagination */
  currentPage: number;

  /** Total pages for conversation list pagination */
  totalPages: number;

  // ---------------------------------------------------------------------------
  // Actions
  // ---------------------------------------------------------------------------

  setSelectedConversation: (id: string | null, name?: string | null) => void;
  setTheme: (theme: 'light' | 'dark') => void;
  setWmVersionFilter: (version: number | null) => void;
  setOverheadFilter: (overhead: number | null) => void;
  setSavingsFilter: (savings: number | null) => void;
  setSortField: (field: DashboardState['selectedSortField']) => void;
  setSortDirection: (direction: DashboardState['selectedSortDirection']) => void;
  setCurrentPage: (page: number) => void;
  setTotalPages: (pages: number) => void;
  resetFilters: () => void;
}

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------

export const useDashboardStore = create<DashboardState>((set) => ({
  // Initial state
  selectedConversationId: null,
  selectedConversationName: null,
  theme: 'light',
  selectedWmVersion: null,
  selectedOverhead: null,
  selectedSavings: null,
  selectedSortField: 'updatedAt',
  selectedSortDirection: 'desc',
  currentPage: 1,
  totalPages: 1,

  // Actions
  setSelectedConversation: (id, name) =>
    set({
      selectedConversationId: id,
      selectedConversationName: name ?? null,
    }),

  setTheme: (theme) => set({ theme }),

  setWmVersionFilter: (version) => set({ selectedWmVersion: version }),

  setOverheadFilter: (overhead) => set({ selectedOverhead: overhead }),

  setSavingsFilter: (savings) => set({ selectedSavings: savings }),

  setSortField: (field) => set({ selectedSortField: field }),

  setSortDirection: (direction) => set({ selectedSortDirection: direction }),

  setCurrentPage: (page) => set({ currentPage: page }),

  setTotalPages: (pages) => set({ totalPages: pages }),

  resetFilters: () =>
    set({
      selectedWmVersion: null,
      selectedOverhead: null,
      selectedSavings: null,
      selectedSortField: 'updatedAt',
      selectedSortDirection: 'desc',
      currentPage: 1,
    }),
}));
