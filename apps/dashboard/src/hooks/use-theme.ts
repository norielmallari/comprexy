/**
 * Hook for managing theme (light/dark) with persistence.
 *
 * Reads from localStorage and syncs with the system preference.
 */

import { useEffect, useLayoutEffect, useState } from 'react';

import { THEME } from '@/lib/constants';

/**
 * Hook to manage theme state with localStorage persistence.
 *
 * Uses deferred initial state to avoid hydration mismatches:
 * initial value is always 'default' (matches server), then useLayoutEffect
 * detects the real theme synchronously before paint.
 *
 * @returns Object containing theme value and toggle function
 */
export function useTheme() {
  const [theme, setThemeState] = useState<'light' | 'dark' | 'default'>('default');

  // Detect and apply theme synchronously before paint to avoid flash
  useLayoutEffect(() => {
    const stored = localStorage.getItem(THEME.STORAGE_KEY);
    const detected =
      stored === 'light' || stored === 'dark'
        ? stored
        : window.matchMedia('(prefers-color-scheme: dark)').matches
          ? 'dark'
          : 'light';

    setThemeState(detected);

    const root = document.documentElement;
    root.classList.remove('light', 'dark');
    root.classList.add(detected);
    localStorage.setItem(THEME.STORAGE_KEY, detected);
  }, []);

  // Apply theme class and persist on explicit changes (toggle)
  useEffect(() => {
    if (theme === 'default') return;

    const root = document.documentElement;
    root.classList.remove('light', 'dark');
    root.classList.add(theme);
    localStorage.setItem(THEME.STORAGE_KEY, theme);
  }, [theme]);

  // Listen for system preference changes
  useEffect(() => {
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    const handler = (e: MediaQueryListEvent) => {
      // Only update if user hasn't explicitly set a theme
      const stored = localStorage.getItem(THEME.STORAGE_KEY);
      if (!stored) {
        setThemeState(e.matches ? 'dark' : 'light');
      }
    };

    mediaQuery.addEventListener('change', handler);
    return () => mediaQuery.removeEventListener('change', handler);
  }, []);

  /**
   * Toggle between light and dark themes.
   */
  const toggleTheme = () => {
    setThemeState((prev) => {
      if (prev === 'default' || prev === 'light') return 'dark';
      return 'light';
    });
  };

  return { theme, toggleTheme };
}
