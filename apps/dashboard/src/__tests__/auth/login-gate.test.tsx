import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { LoginGate } from '@/components/auth/login-gate';
import {
  clearDashboardApiKey,
  getDashboardApiKey,
  notifyAuthRequired,
  setDashboardApiKey,
} from '@/lib/auth/dashboard-api-key';

describe('LoginGate', () => {
  beforeEach(() => {
    clearDashboardApiKey();
    sessionStorage.clear();
  });

  afterEach(() => {
    clearDashboardApiKey();
    sessionStorage.clear();
  });

  it('opens on auth-required notify when uncontrolled', async () => {
    render(<LoginGate />);

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    notifyAuthRequired();

    await waitFor(() => {
      expect(screen.getByRole('dialog', { name: 'Dashboard API key' })).toBeInTheDocument();
    });
    expect(screen.getByRole('alert')).toHaveTextContent(/API key required/i);
  });

  it('Save key stores the value and calls onAuthenticated', async () => {
    const onAuthenticated = vi.fn();
    const onOpenChange = vi.fn();

    render(
      <LoginGate open onOpenChange={onOpenChange} onAuthenticated={onAuthenticated} />,
    );

    fireEvent.change(screen.getByLabelText('API key'), {
      target: { value: '  synthetic-dashboard-key  ' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save key' }));

    expect(getDashboardApiKey()).toBe('synthetic-dashboard-key');
    expect(onAuthenticated).toHaveBeenCalledTimes(1);
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('Clear key removes storage and calls onCleared', () => {
    setDashboardApiKey('synthetic-dashboard-key');
    const onCleared = vi.fn();

    render(<LoginGate open onCleared={onCleared} />);

    fireEvent.click(screen.getByRole('button', { name: 'Clear key' }));

    expect(getDashboardApiKey()).toBeNull();
    expect(onCleared).toHaveBeenCalledTimes(1);
  });

  it('rejects empty key without storing', () => {
    render(<LoginGate open />);

    fireEvent.change(screen.getByLabelText('API key'), { target: { value: '   ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save key' }));

    expect(getDashboardApiKey()).toBeNull();
    expect(screen.getByRole('alert')).toHaveTextContent(/non-empty/i);
  });

  it('focuses the API key input when opened', async () => {
    render(<LoginGate open />);

    await waitFor(() => {
      expect(screen.getByLabelText('API key')).toHaveFocus();
    });
  });

  it('Tab from last control wraps focus to the API key input', async () => {
    render(<LoginGate open />);

    await waitFor(() => {
      expect(screen.getByLabelText('API key')).toHaveFocus();
    });

    const dismiss = screen.getByRole('button', { name: 'Dismiss' });
    dismiss.focus();
    expect(dismiss).toHaveFocus();

    fireEvent.keyDown(document, { key: 'Tab' });

    expect(screen.getByLabelText('API key')).toHaveFocus();
  });

  it('Escape calls onOpenChange(false)', async () => {
    const onOpenChange = vi.fn();
    render(<LoginGate open onOpenChange={onOpenChange} />);

    await waitFor(() => {
      expect(screen.getByLabelText('API key')).toHaveFocus();
    });

    fireEvent.keyDown(document, { key: 'Escape' });

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('backdrop click dismisses; dialog click does not', async () => {
    const onOpenChange = vi.fn();
    render(<LoginGate open onOpenChange={onOpenChange} />);

    await waitFor(() => {
      expect(screen.getByRole('dialog')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('dialog'));
    expect(onOpenChange).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('presentation'));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
