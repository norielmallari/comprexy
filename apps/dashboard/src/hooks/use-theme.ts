/**
 * Hook for managing theme (light/dark) with persistence.
 *
 * Reads from localStorage and syncs with the system preference.
 */

import { useEffect, useState } from 'react';

import { THEME } from '@/lib/constants';

/**
 * Hook to manage theme state with localStorage persistence.
 *
 * @returns Object containing theme value and toggle function
 */
export function useTheme() {
  const [theme, setThemeState] = useState<'light' | 'dark'>(() => {
    // Check localStorage first
    if (typeof window !== 'undefined') {
      const stored = localStorage.getItem(THEME.STORAGE_KEY);
      if (stored === 'light' || stored === 'dark') {
        return stored;
      }
    }
    // Fall back to system preference
    if (typeof window !== 'undefined') {
      return window.matchMedia('(prefers-color-scheme: dark)').matches
        ? 'dark'
        : 'light';
    }
    return 'light';
  });

  // Apply theme to document class
  useEffect(() => {
    const root = document.documentElement;
    root.classList.remove('light', 'dark');
    root.classList.add(theme);

    // Persist to localStorage
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
    setThemeState((prev) => (prev === 'light' ? 'dark' : 'light'));
  };

  return { theme, toggleTheme };
}
