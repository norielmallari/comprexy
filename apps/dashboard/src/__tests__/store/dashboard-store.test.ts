import { useDashboardStore } from '@/lib/store/dashboard-store';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function resetStore() {
  useDashboardStore.setState({
    selectedConversationId: null,
    selectedConversationName: null,
    theme: 'light',
    selectedCostModelKey: 'local',
    selectedWmVersion: null,
    selectedOverhead: null,
    selectedSavings: null,
    selectedSortField: 'updatedAt',
    selectedSortDirection: 'desc',
    currentPage: 1,
    totalPages: 1,
  });
}

// ---------------------------------------------------------------------------
// Initial state
// ---------------------------------------------------------------------------

describe('dashboard store', () => {
  beforeEach(resetStore);

  it('has correct initial state values', () => {
    const state = useDashboardStore.getState();

    expect(state.selectedConversationId).toBeNull();
    expect(state.selectedConversationName).toBeNull();
    expect(state.theme).toBe('light');
    expect(state.selectedCostModelKey).toBe('local');
    expect(state.selectedWmVersion).toBeNull();
    expect(state.selectedOverhead).toBeNull();
    expect(state.selectedSavings).toBeNull();
    expect(state.selectedSortField).toBe('updatedAt');
    expect(state.selectedSortDirection).toBe('desc');
    expect(state.currentPage).toBe(1);
    expect(state.totalPages).toBe(1);
  });
});

// ---------------------------------------------------------------------------
// setSelectedCostModelKey()
// ---------------------------------------------------------------------------

describe('setSelectedCostModelKey()', () => {
  beforeEach(() => {
    resetStore();
    sessionStorage.clear();
  });

  it('updates selectedCostModelKey and persists to sessionStorage', () => {
    useDashboardStore.getState().setSelectedCostModelKey('claude-sonnet-5');

    expect(useDashboardStore.getState().selectedCostModelKey).toBe('claude-sonnet-5');
    expect(sessionStorage.getItem('comprexy.selectedCostModelKey')).toBe('claude-sonnet-5');
  });
});

// ---------------------------------------------------------------------------
// setSelectedConversation()
// ---------------------------------------------------------------------------

describe('setSelectedConversation()', () => {
  beforeEach(resetStore);

  it('updates state with id and name', () => {
    useDashboardStore.getState().setSelectedConversation('conv-123', 'My Chat');

    const state = useDashboardStore.getState();
    expect(state.selectedConversationId).toBe('conv-123');
    expect(state.selectedConversationName).toBe('My Chat');
  });

  it('updates state with id only (name defaults to null)', () => {
    useDashboardStore.getState().setSelectedConversation('conv-123');

    const state = useDashboardStore.getState();
    expect(state.selectedConversationId).toBe('conv-123');
    expect(state.selectedConversationName).toBeNull();
  });

  it('clears conversation when id is null', () => {
    useDashboardStore.getState().setSelectedConversation('conv-123', 'My Chat');
    useDashboardStore.getState().setSelectedConversation(null);

    const state = useDashboardStore.getState();
    expect(state.selectedConversationId).toBeNull();
    expect(state.selectedConversationName).toBeNull();
  });

  it('updates conversation when name is explicitly null', () => {
    useDashboardStore.getState().setSelectedConversation('conv-123', 'My Chat');
    useDashboardStore.getState().setSelectedConversation('conv-456', null);

    const state = useDashboardStore.getState();
    expect(state.selectedConversationId).toBe('conv-456');
    expect(state.selectedConversationName).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// setTheme()
// ---------------------------------------------------------------------------

describe('setTheme()', () => {
  beforeEach(resetStore);

  it('updates theme to dark', () => {
    useDashboardStore.getState().setTheme('dark');
    expect(useDashboardStore.getState().theme).toBe('dark');
  });

  it('updates theme to light', () => {
    useDashboardStore.getState().setTheme('dark');
    useDashboardStore.getState().setTheme('light');
    expect(useDashboardStore.getState().theme).toBe('light');
  });
});

// ---------------------------------------------------------------------------
// setWmVersionFilter()
// ---------------------------------------------------------------------------

describe('setWmVersionFilter()', () => {
  beforeEach(resetStore);

  it('sets a version filter', () => {
    useDashboardStore.getState().setWmVersionFilter(2);
    expect(useDashboardStore.getState().selectedWmVersion).toBe(2);
  });

  it('clears the filter when set to null', () => {
    useDashboardStore.getState().setWmVersionFilter(2);
    useDashboardStore.getState().setWmVersionFilter(null);
    expect(useDashboardStore.getState().selectedWmVersion).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// setOverheadFilter()
// ---------------------------------------------------------------------------

describe('setOverheadFilter()', () => {
  beforeEach(resetStore);

  it('sets an overhead filter', () => {
    useDashboardStore.getState().setOverheadFilter(500);
    expect(useDashboardStore.getState().selectedOverhead).toBe(500);
  });

  it('clears the filter when set to null', () => {
    useDashboardStore.getState().setOverheadFilter(500);
    useDashboardStore.getState().setOverheadFilter(null);
    expect(useDashboardStore.getState().selectedOverhead).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// setSavingsFilter()
// ---------------------------------------------------------------------------

describe('setSavingsFilter()', () => {
  beforeEach(resetStore);

  it('sets a savings filter', () => {
    useDashboardStore.getState().setSavingsFilter(1000);
    expect(useDashboardStore.getState().selectedSavings).toBe(1000);
  });

  it('clears the filter when set to null', () => {
    useDashboardStore.getState().setSavingsFilter(1000);
    useDashboardStore.getState().setSavingsFilter(null);
    expect(useDashboardStore.getState().selectedSavings).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// setSortField()
// ---------------------------------------------------------------------------

describe('setSortField()', () => {
  beforeEach(resetStore);

  it('updates sort field to totalTurns', () => {
    useDashboardStore.getState().setSortField('totalTurns');
    expect(useDashboardStore.getState().selectedSortField).toBe('totalTurns');
  });

  it('updates sort field to totalNetTokensSaved', () => {
    useDashboardStore.getState().setSortField('totalNetTokensSaved');
    expect(useDashboardStore.getState().selectedSortField).toBe('totalNetTokensSaved');
  });

  it('updates sort field to updatedAt', () => {
    useDashboardStore.getState().setSortField('updatedAt');
    expect(useDashboardStore.getState().selectedSortField).toBe('updatedAt');
  });
});

// ---------------------------------------------------------------------------
// setSortDirection()
// ---------------------------------------------------------------------------

describe('setSortDirection()', () => {
  beforeEach(resetStore);

  it('updates sort direction to asc', () => {
    useDashboardStore.getState().setSortDirection('asc');
    expect(useDashboardStore.getState().selectedSortDirection).toBe('asc');
  });

  it('updates sort direction to desc', () => {
    useDashboardStore.getState().setSortDirection('asc');
    useDashboardStore.getState().setSortDirection('desc');
    expect(useDashboardStore.getState().selectedSortDirection).toBe('desc');
  });
});

// ---------------------------------------------------------------------------
// setCurrentPage()
// ---------------------------------------------------------------------------

describe('setCurrentPage()', () => {
  beforeEach(resetStore);

  it('updates current page', () => {
    useDashboardStore.getState().setCurrentPage(5);
    expect(useDashboardStore.getState().currentPage).toBe(5);
  });
});

// ---------------------------------------------------------------------------
// setTotalPages()
// ---------------------------------------------------------------------------

describe('setTotalPages()', () => {
  beforeEach(resetStore);

  it('updates total pages', () => {
    useDashboardStore.getState().setTotalPages(10);
    expect(useDashboardStore.getState().totalPages).toBe(10);
  });
});

// ---------------------------------------------------------------------------
// resetFilters()
// ---------------------------------------------------------------------------

describe('resetFilters()', () => {
  beforeEach(resetStore);

  it('resets to defaults', () => {
    useDashboardStore.getState().setWmVersionFilter(2);
    useDashboardStore.getState().setOverheadFilter(500);
    useDashboardStore.getState().setSavingsFilter(1000);
    useDashboardStore.getState().setSortField('totalTurns');
    useDashboardStore.getState().setSortDirection('asc');
    useDashboardStore.getState().setCurrentPage(5);

    useDashboardStore.getState().resetFilters();

    const state = useDashboardStore.getState();
    expect(state.selectedWmVersion).toBeNull();
    expect(state.selectedOverhead).toBeNull();
    expect(state.selectedSavings).toBeNull();
    expect(state.selectedSortField).toBe('updatedAt');
    expect(state.selectedSortDirection).toBe('desc');
    expect(state.currentPage).toBe(1);
  });

  it('does not reset selected conversation or theme', () => {
    useDashboardStore.getState().setSelectedConversation('conv-123', 'My Chat');
    useDashboardStore.getState().setTheme('dark');
    useDashboardStore.getState().resetFilters();

    const state = useDashboardStore.getState();
    expect(state.selectedConversationId).toBe('conv-123');
    expect(state.selectedConversationName).toBe('My Chat');
    expect(state.theme).toBe('dark');
  });

  it('does not reset totalPages', () => {
    useDashboardStore.getState().setTotalPages(10);
    useDashboardStore.getState().resetFilters();

    expect(useDashboardStore.getState().totalPages).toBe(10);
  });
});
