/**
 * Login gate: prompts for Auth:DashboardApiKey on 401 or when opened manually.
 */

'use client';

import {
  FormEvent,
  useCallback,
  useEffect,
  useId,
  useRef,
  useState,
} from 'react';

import { Button } from '@/components/ui/button';
import {
  clearDashboardApiKey,
  getDashboardApiKey,
  onAuthRequired,
  setDashboardApiKey,
} from '@/lib/auth/dashboard-api-key';

interface LoginGateProps {
  /** Controlled open state from the shell (optional). */
  open?: boolean;
  /** Called when the dialog should close after a successful save or dismiss. */
  onOpenChange?: (open: boolean) => void;
  /** Called after a key is saved so queries can retry. */
  onAuthenticated?: () => void;
  /** Called after the key is cleared. */
  onCleared?: () => void;
}

const FOCUSABLE_SELECTOR =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

export function LoginGate({
  open: controlledOpen,
  onOpenChange,
  onAuthenticated,
  onCleared,
}: LoginGateProps) {
  const inputId = useId();
  const dialogRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const previouslyFocusedRef = useRef<HTMLElement | null>(null);
  const [internalOpen, setInternalOpen] = useState(false);
  const [keyInput, setKeyInput] = useState('');
  const [error, setError] = useState<string | null>(null);

  const isControlled = controlledOpen !== undefined;
  const isOpen = isControlled ? controlledOpen : internalOpen;

  const setOpen = useCallback(
    (next: boolean) => {
      if (!isControlled) {
        setInternalOpen(next);
      }
      onOpenChange?.(next);
    },
    [isControlled, onOpenChange],
  );

  useEffect(() => {
    return onAuthRequired(() => {
      setError('API key required or invalid. Enter Auth:DashboardApiKey to continue.');
      setOpen(true);
    });
  }, [setOpen]);

  useEffect(() => {
    if (isOpen) {
      setKeyInput(getDashboardApiKey() ?? '');
    }
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) {
      return;
    }

    previouslyFocusedRef.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;

    const focusInput = () => {
      inputRef.current?.focus();
    };
    // Defer so the dialog is in the DOM before focusing.
    const focusTimer = window.setTimeout(focusInput, 0);

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        event.stopPropagation();
        setOpen(false);
        return;
      }

      if (event.key !== 'Tab') {
        return;
      }

      const dialog = dialogRef.current;
      if (!dialog) {
        return;
      }

      const focusable = Array.from(
        dialog.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
      ).filter((el) => !el.hasAttribute('disabled') && el.tabIndex !== -1);

      if (focusable.length === 0) {
        event.preventDefault();
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      const active = document.activeElement;

      if (event.shiftKey) {
        if (active === first || !dialog.contains(active)) {
          event.preventDefault();
          last.focus();
        }
      } else if (active === last || !dialog.contains(active)) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', handleKeyDown, true);

    return () => {
      window.clearTimeout(focusTimer);
      document.removeEventListener('keydown', handleKeyDown, true);
      const previous = previouslyFocusedRef.current;
      if (previous && typeof previous.focus === 'function') {
        previous.focus();
      }
    };
  }, [isOpen, setOpen]);

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    const trimmed = keyInput.trim();
    if (!trimmed) {
      setError('Enter a non-empty API key.');
      return;
    }
    setDashboardApiKey(trimmed);
    setError(null);
    setOpen(false);
    onAuthenticated?.();
  };

  const handleClear = () => {
    clearDashboardApiKey();
    setKeyInput('');
    setError(null);
    onCleared?.();
  };

  const handleDismiss = () => {
    setOpen(false);
  };

  if (!isOpen) {
    return null;
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
      role="presentation"
      onClick={handleDismiss}
    >
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="dashboard-login-title"
        className="w-full max-w-md rounded-lg border border-border bg-card p-6 shadow-lg"
        data-testid="login-gate"
        onClick={(event) => event.stopPropagation()}
      >
        <h2 id="dashboard-login-title" className="text-lg font-semibold text-foreground">
          Dashboard API key
        </h2>
        <p className="mt-2 text-sm text-muted-foreground">
          Enter the control-api <code className="text-xs">Auth:DashboardApiKey</code> value.
          It is stored in this tab&apos;s session only and sent as Bearer / X-Api-Key on{' '}
          <code className="text-xs">/v1/*</code> requests. Health checks stay unauthenticated.
        </p>

        <form className="mt-4 space-y-3" onSubmit={handleSubmit}>
          <div>
            <label htmlFor={inputId} className="block text-sm font-medium text-foreground">
              API key
            </label>
            <input
              ref={inputRef}
              id={inputId}
              type="password"
              autoComplete="off"
              value={keyInput}
              onChange={(e) => setKeyInput(e.target.value)}
              className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm text-foreground"
              placeholder="Dashboard API key"
            />
          </div>

          {error && (
            <p className="text-sm text-red-600 dark:text-red-400" role="alert">
              {error}
            </p>
          )}

          <div className="flex flex-wrap items-center gap-2">
            <Button type="submit" size="sm">
              Save key
            </Button>
            <Button type="button" size="sm" variant="secondary" onClick={handleClear}>
              Clear key
            </Button>
            <Button type="button" size="sm" variant="ghost" onClick={handleDismiss}>
              Dismiss
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}
