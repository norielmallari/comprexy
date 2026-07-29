import { renderHook, act } from '@testing-library/react';
import { useTheme } from '@/hooks/use-theme';

const THEME_KEY = 'comprexy-theme';

// Helper to set up localStorage mock with an initial value
function setupLocalStorageWithValue(value: string | null) {
  const localStorageMock = localStorage as any;
  if (value !== null) {
    localStorageMock.setItem(THEME_KEY, value);
  }
  // Re-implement getItem to read from the mock's internal store
  localStorageMock.getItem = (key: string) => (key === THEME_KEY ? value : null);
}

describe('useTheme', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Reset localStorage mock
    const localStorageMock = localStorage as any;
    localStorageMock.store = {};
    localStorageMock.getItem = vi.fn((key: string) => localStorageMock.store[key] || null);
    localStorageMock.setItem = vi.fn((key: string, value: string) => {
      localStorageMock.store[key] = value;
    });
    localStorageMock.removeItem = vi.fn((key: string) => {
      delete localStorageMock.store[key];
    });
    localStorageMock.clear = vi.fn(() => {
      localStorageMock.store = {};
    });
  });

  it('returns default theme light when no localStorage', () => {
    const { result } = renderHook(() => useTheme());
    expect(result.current.theme).toBe('light');
  });

  it('returns stored theme from localStorage', () => {
    const localStorageMock = localStorage as any;
    localStorageMock.store = { [THEME_KEY]: 'dark' };
    localStorageMock.getItem = vi.fn((key: string) => localStorageMock.store[key] || null);

    const { result } = renderHook(() => useTheme());
    expect(result.current.theme).toBe('dark');
  });

  it('returns light theme from localStorage when stored', () => {
    const localStorageMock = localStorage as any;
    localStorageMock.store = { [THEME_KEY]: 'light' };
    localStorageMock.getItem = vi.fn((key: string) => localStorageMock.store[key] || null);

    const { result } = renderHook(() => useTheme());
    expect(result.current.theme).toBe('light');
  });

  it('toggles between light and dark', () => {
    const { result, rerender } = renderHook(() => useTheme());

    expect(result.current.theme).toBe('light');

    act(() => {
      result.current.toggleTheme();
    });
    rerender();

    expect(result.current.theme).toBe('dark');

    act(() => {
      result.current.toggleTheme();
    });
    rerender();

    expect(result.current.theme).toBe('light');
  });

  it('applies theme class to documentElement', () => {
    renderHook(() => useTheme());

    expect(document.documentElement.classList.contains('light')).toBe(true);
    expect(document.documentElement.classList.contains('dark')).toBe(false);
  });

  it('applies dark class when theme is dark', () => {
    const localStorageMock = localStorage as any;
    localStorageMock.store = { [THEME_KEY]: 'dark' };
    localStorageMock.getItem = vi.fn((key: string) => localStorageMock.store[key] || null);

    renderHook(() => useTheme());

    expect(document.documentElement.classList.contains('dark')).toBe(true);
    expect(document.documentElement.classList.contains('light')).toBe(false);
  });

  it('removes old theme class when toggling', () => {
    const localStorageMock = localStorage as any;
    localStorageMock.store = { [THEME_KEY]: 'dark' };
    localStorageMock.getItem = vi.fn((key: string) => localStorageMock.store[key] || null);

    const { result, rerender } = renderHook(() => useTheme());

    expect(document.documentElement.classList.contains('dark')).toBe(true);

    act(() => {
      result.current.toggleTheme();
    });
    rerender();

    expect(document.documentElement.classList.contains('light')).toBe(true);
    expect(document.documentElement.classList.contains('dark')).toBe(false);
  });

  it('persists theme to localStorage on toggle', () => {
    const localStorageMock = localStorage as any;
    localStorageMock.store = {};

    const { result, rerender } = renderHook(() => useTheme());

    expect(result.current.theme).toBe('light');

    act(() => {
      result.current.toggleTheme();
    });
    rerender();

    expect(localStorageMock.setItem).toHaveBeenCalledWith(THEME_KEY, 'dark');
  });

  it('persists theme to localStorage on initial render with dark', () => {
    const localStorageMock = localStorage as any;
    localStorageMock.store = {};
    localStorageMock.getItem = vi.fn((key: string) => localStorageMock.store[key] || null);

    localStorageMock.store = { [THEME_KEY]: 'dark' };

    renderHook(() => useTheme());

    expect(localStorageMock.setItem).toHaveBeenCalledWith(THEME_KEY, 'dark');
  });

  it('respects system preference when no stored value and system is dark', () => {
    const mediaQueryMock = { matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() };

    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: vi.fn().mockImplementation((query: string) =>
        query === '(prefers-color-scheme: dark)' ? mediaQueryMock : { matches: false },
      ),
    });

    const { result } = renderHook(() => useTheme());
    expect(result.current.theme).toBe('dark');
  });

  it('respects system preference when no stored value and system is light', () => {
    const mediaQueryMock = { matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() };

    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: vi.fn().mockImplementation((query: string) =>
        query === '(prefers-color-scheme: dark)' ? mediaQueryMock : { matches: false },
      ),
    });

    const { result } = renderHook(() => useTheme());
    expect(result.current.theme).toBe('light');
  });

  it('prefers stored value over system preference', () => {
    const mediaQueryMock = { matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() };

    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: vi.fn().mockImplementation((query: string) =>
        query === '(prefers-color-scheme: dark)' ? mediaQueryMock : { matches: false },
      ),
    });

    const localStorageMock = localStorage as any;
    localStorageMock.store = { [THEME_KEY]: 'light' };
    localStorageMock.getItem = vi.fn((key: string) => localStorageMock.store[key] || null);

    const { result } = renderHook(() => useTheme());
    expect(result.current.theme).toBe('light');
  });

  it('returns toggleTheme function', () => {
    const { result } = renderHook(() => useTheme());
    expect(typeof result.current.toggleTheme).toBe('function');
  });

  it('returns theme string', () => {
    const { result } = renderHook(() => useTheme());
    expect(['light', 'dark']).toContain(result.current.theme);
  });
});
