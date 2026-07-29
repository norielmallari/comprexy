/**
 * Top bar with conversation selector, theme toggle, and status indicator.
 */

'use client';

import { useEffect, useState } from 'react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Select } from '@/components/ui/select';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { useTheme } from '@/hooks/use-theme';
import { useConversations } from '@/lib/queries/use-conversations';
import { useConversationUrl } from '@/hooks/use-conversation-url';
import { truncateConversationId } from '@/lib/utils';
import { API_BASE_URL } from '@/lib/constants';

/**
 * TopBar component with conversation selector, theme toggle, and status indicator.
 */
export function TopBar() {
  const { theme, toggleTheme } = useTheme();
  const { data: conversations, isLoading: conversationsLoading } = useConversations();
  const { conversationId, navigateToConversation } = useConversationUrl();
  const [apiHealthy, setApiHealthy] = useState<boolean | null>(null);

  // Check API health
  useEffect(() => {
    const checkHealth = async () => {
      try {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 2000);
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

  const handleConversationChange = (value: string) => {
    if (value === 'none') {
      navigateToConversation(null);
    } else {
      navigateToConversation(value);
    }
  };

  return (
    <header className="flex h-16 shrink-0 items-center justify-between border-b border-border bg-card px-6">
      {/* Left: Title + Conversation Selector */}
      <div className="flex items-center gap-4">
        <h1 className="text-lg font-semibold text-foreground">Comprexy Metrics</h1>

        <div className="h-6 w-px bg-border" />

        <div className="flex items-center gap-2">
          <span className="text-sm text-muted-foreground">Conversation:</span>
          <Select
            options={
              conversations?.map((c) => ({
                label: truncateConversationId(c.conversationId),
                value: c.conversationId,
              })) ?? []
            }
            value={conversationId ?? 'none'}
            placeholder={conversationsLoading ? 'Loading...' : 'Select conversation'}
            onChange={handleConversationChange}
            className="w-48"
            disabled={conversationsLoading}
          />
        </div>
      </div>

      {/* Right: Status + Theme Toggle */}
      <div className="flex items-center gap-4">
        {/* API Health Indicator */}
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
            <p>API Status: {apiHealthy === true ? 'Healthy' : apiHealthy === false ? 'Unreachable' : 'Checking...'}</p>
          </TooltipContent>
        </Tooltip>

        {/* Conversation ID Display */}
        {conversationId && (
          <Badge variant="info" className="font-mono text-xs">
            {truncateConversationId(conversationId)}
          </Badge>
        )}

        {/* Theme Toggle */}
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
      </div>
    </header>
  );
}
