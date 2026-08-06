/**
 * Top bar with navigation, conversation selector (metrics only), cost picker,
 * theme toggle, API key control, and status.
 */

'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useEffect, useState } from 'react';

import { CostModelPicker } from '@/components/cost/cost-model-picker';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Select } from '@/components/ui/select';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { useTheme } from '@/hooks/use-theme';
import { useConversations } from '@/lib/queries/use-conversations';
import { useConversationUrl } from '@/hooks/use-conversation-url';
import { getDashboardApiKey } from '@/lib/auth/dashboard-api-key';
import { truncateConversationId, cn, encodeConversationId } from '@/lib/utils';
import { API_BASE_URL } from '@/lib/constants';

function buildNavHref(path: string, conversationId: string | null): string {
  if (!conversationId) {
    return path;
  }

  const params = new URLSearchParams({ conv: encodeConversationId(conversationId) });
  return `${path}?${params.toString()}`;
}

function useIsClient() {
  const [isClient, setIsClient] = useState(false);
  useEffect(() => setIsClient(true), []);
  return isClient;
}

interface TopBarProps {
  onRequestLogin?: () => void;
}

export function TopBar({ onRequestLogin }: TopBarProps) {
  const pathname = usePathname();
  const isBenchmarkPage = pathname?.startsWith('/benchmark') ?? false;
  const isSettingsPage = pathname?.startsWith('/settings') ?? false;
  const pageTitle = isBenchmarkPage
    ? 'Comprexy Benchmark'
    : isSettingsPage
      ? 'Comprexy Settings'
      : 'Comprexy Metrics';

  const { theme, toggleTheme } = useTheme();
  const { data: conversations, isLoading: conversationsLoading } = useConversations();
  const { conversationId, effectiveConversationId, navigateToConversation } = useConversationUrl();
  const [apiHealthy, setApiHealthy] = useState<boolean | null>(null);
  const [hasApiKey, setHasApiKey] = useState(false);
  const isClient = useIsClient();

  useEffect(() => {
    const checkHealth = async () => {
      try {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 2000);
        // /health stays unauthenticated — raw fetch, not apiFetch
        const response = await fetch(`${API_BASE_URL}/health`, { signal: controller.signal });
        clearTimeout(timeout);
        setApiHealthy(response.ok);
      } catch {
        setApiHealthy(false);
      }
    };

    checkHealth();
    const interval = setInterval(checkHealth, 30000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    if (!isClient) {
      return;
    }
    setHasApiKey(Boolean(getDashboardApiKey()));
    const onStorage = () => setHasApiKey(Boolean(getDashboardApiKey()));
    window.addEventListener('storage', onStorage);
    const interval = setInterval(onStorage, 2000);
    return () => {
      window.removeEventListener('storage', onStorage);
      clearInterval(interval);
    };
  }, [isClient]);

  const handleConversationChange = (value: string) => {
    if (value === 'none') {
      navigateToConversation(null);
    } else {
      navigateToConversation(value);
    }
  };

  const showConversationSelector = !isBenchmarkPage && !isSettingsPage;

  return (
    <header className="flex h-16 shrink-0 items-center justify-between border-b border-border bg-card px-6">
      <div className="flex items-center gap-4">
        <h1 className="text-lg font-semibold text-foreground">{pageTitle}</h1>

        <nav aria-label="Dashboard navigation" className="flex items-center gap-1">
          <Link
            href={buildNavHref('/', effectiveConversationId)}
            className={cn(
              'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
              !isBenchmarkPage && !isSettingsPage
                ? 'bg-primary/10 text-foreground'
                : 'text-muted-foreground hover:text-foreground',
            )}
            data-testid="nav-metrics"
          >
            Metrics
          </Link>
          <Link
            href={buildNavHref('/benchmark', effectiveConversationId)}
            className={cn(
              'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
              isBenchmarkPage
                ? 'bg-primary/10 text-foreground'
                : 'text-muted-foreground hover:text-foreground',
            )}
            data-testid="nav-benchmark"
          >
            Benchmark
          </Link>
          <Link
            href="/settings"
            className={cn(
              'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
              isSettingsPage
                ? 'bg-primary/10 text-foreground'
                : 'text-muted-foreground hover:text-foreground',
            )}
            data-testid="nav-settings"
          >
            Settings
          </Link>
        </nav>

        {showConversationSelector && (
          <>
            <div className="h-6 w-px bg-border" />

            <div className="flex items-center gap-2">
              <span className="text-sm text-muted-foreground">Conversation:</span>
              <Select
                options={[
                  {
                    value: 'none',
                    label: conversationsLoading ? 'Loading...' : 'Select conversation',
                  },
                  ...(conversations?.map((c) => ({
                    label: truncateConversationId(c.conversationId),
                    value: c.conversationId,
                  })) ?? []),
                ]}
                value={conversationId ?? 'none'}
                onChange={handleConversationChange}
                className="w-48"
                disabled={conversationsLoading}
              />
            </div>
          </>
        )}
      </div>

      <div className="flex items-center gap-4">
        {isClient && <CostModelPicker />}

        {isClient && (
          <Tooltip delayDuration={200}>
            <TooltipTrigger asChild>
              <div className="flex items-center gap-2">
                <div
                  className={`h-2 w-2 rounded-full ${
                    apiHealthy === true
                      ? 'bg-green-500'
                      : apiHealthy === false
                        ? 'bg-red-500'
                        : 'bg-yellow-500 animate-pulse'
                  }`}
                />
                <span className="text-xs text-muted-foreground">
                  {apiHealthy === true
                    ? 'Connected'
                    : apiHealthy === false
                      ? 'Disconnected'
                      : 'Connecting'}
                </span>
              </div>
            </TooltipTrigger>
            <TooltipContent side="bottom">
              <p>
                API Status:{' '}
                {apiHealthy === true
                  ? 'Healthy'
                  : apiHealthy === false
                    ? 'Unreachable'
                    : 'Checking...'}
              </p>
            </TooltipContent>
          </Tooltip>
        )}

        {showConversationSelector && conversationId && (
          <Badge variant="info" className="font-mono text-xs">
            {truncateConversationId(conversationId)}
          </Badge>
        )}

        {isClient && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={onRequestLogin}
            aria-label={hasApiKey ? 'Manage dashboard API key' : 'Enter dashboard API key'}
            data-testid="api-key-control"
          >
            {hasApiKey ? 'API key' : 'Sign in'}
          </Button>
        )}

        {isClient && (
          <Tooltip delayDuration={200}>
            <TooltipTrigger asChild>
              <Button variant="ghost" size="icon" onClick={toggleTheme} aria-label="Toggle theme">
                {theme === 'dark' ? (
                  <svg
                    xmlns="http://www.w3.org/2000/svg"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    className="h-5 w-5"
                  >
                    <circle cx="12" cy="12" r="4" />
                    <path d="M12 2v2" />
                    <path d="M12 20v2" />
                    <path d="m4.93 4.93 1.41 1.41" />
                    <path d="m17.66 17.66 1.41 1.41" />
                    <path d="M2 12h2" />
                    <path d="M20 12h2" />
                    <path d="m6.34 17.66-1.41 1.41" />
                    <path d="m19.07 4.93-1.41 1.41" />
                  </svg>
                ) : (
                  <svg
                    xmlns="http://www.w3.org/2000/svg"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    className="h-5 w-5"
                  >
                    <path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z" />
                  </svg>
                )}
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              <p>Switch to {theme === 'dark' ? 'light' : 'dark'} mode</p>
            </TooltipContent>
          </Tooltip>
        )}
      </div>
    </header>
  );
}
