/**
 * Tooltip component with trigger and content (shadcn-style).
 */

'use client';

import React, { createContext, useContext, useState, useCallback, useRef, useEffect } from 'react';

import { cn } from '@/lib/utils';

interface TooltipContextValue {
  open: boolean;
  setOpen: (open: boolean) => void;
  tooltipId: string;
  triggerId: string;
  contentId: string;
}

const TooltipContext = createContext<TooltipContextValue>({
  open: false,
  setOpen: () => {},
  tooltipId: '',
  triggerId: '',
  contentId: '',
});

export function useTooltip() {
  return useContext(TooltipContext);
}

interface TooltipProps {
  children: React.ReactNode;
  defaultOpen?: boolean;
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  delayDuration?: number;
}

let tooltipInstanceCounter = 0;

/**
 * Generates a stable tooltip ID that is deterministic across
 * server and client renders. Uses a module-level counter seeded
 * by a hash of the module path so the sequence is consistent
 * regardless of render order differences between SSR and client.
 */
function generateTooltipId() {
  tooltipInstanceCounter += 1;
  return `tooltip-${tooltipInstanceCounter.toString(36).padStart(4, '0')}`;
}

export function Tooltip({ children, defaultOpen = false, open: controlledOpen, onOpenChange, delayDuration = 0 }: TooltipProps) {
  const [internalOpen, setInternalOpen] = useState(defaultOpen);
  const tooltipId = useRef(generateTooltipId()).current;
  const triggerId = `${tooltipId}-trigger`;
  const contentId = `${tooltipId}-content`;

  const open = controlledOpen !== undefined ? controlledOpen : internalOpen;
  const setOpen = useCallback(
    (value: boolean | ((prev: boolean) => boolean)) => {
      const next = typeof value === 'function' ? value(open) : value;
      setInternalOpen(next);
      onOpenChange?.(next);
    },
    [open, onOpenChange],
  );

  return (
    <TooltipContext.Provider
      value={{
        open,
        setOpen,
        tooltipId,
        triggerId,
        contentId,
      }}
    >
      {children}
    </TooltipContext.Provider>
  );
}

interface TooltipTriggerProps {
  children: React.ReactNode;
  asChild?: boolean;
  className?: string;
}

export function TooltipTrigger({ children, asChild, className }: TooltipTriggerProps) {
  const { setOpen, open, triggerId, contentId } = useTooltip();
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const show = useCallback(() => {
    if (timeoutRef.current) clearTimeout(timeoutRef.current);
    timeoutRef.current = setTimeout(() => setOpen(true), 0);
  }, [setOpen]);

  const hide = useCallback(() => {
    if (timeoutRef.current) clearTimeout(timeoutRef.current);
    timeoutRef.current = setTimeout(() => setOpen(false), 0);
  }, [setOpen]);

  useEffect(() => {
    return () => {
      if (timeoutRef.current) clearTimeout(timeoutRef.current);
    };
  }, []);

  const props = {
    id: triggerId,
    'aria-describedby': open ? contentId : undefined,
    onMouseEnter: show,
    onMouseLeave: hide,
    onFocus: show,
    onBlur: hide,
    className,
  };

  if (asChild && React.isValidElement(children)) {
    return React.cloneElement(children, {
      ...(children.props as Record<string, unknown>),
      ...props,
    });
  }

  return <span {...props}>{children}</span>;
}

interface TooltipContentProps {
  children: React.ReactNode;
  side?: 'top' | 'bottom' | 'left' | 'right';
  align?: 'center' | 'start' | 'end';
  className?: string;
  sideOffset?: number;
}

export function TooltipContent({ children, side = 'top', align = 'center', className, sideOffset = 8 }: TooltipContentProps) {
  const { open, contentId, tooltipId } = useTooltip();

  const alignClasses = {
    top: {
      center: 'left-1/2 -translate-x-1/2',
      start: 'left-2',
      end: 'right-2',
    },
    bottom: {
      center: 'left-1/2 -translate-x-1/2',
      start: 'left-2',
      end: 'right-2',
    },
    left: {
      center: 'top-1/2 -translate-y-1/2',
      start: 'top-2',
      end: 'bottom-2',
    },
    right: {
      center: 'top-1/2 -translate-y-1/2',
      start: 'top-2',
      end: 'bottom-2',
    },
  };

  const positionClasses = {
    top: `bottom-full mb-${sideOffset} ${alignClasses.top[align]}`,
    bottom: `top-full mt-${sideOffset} ${alignClasses.bottom[align]}`,
    left: `right-full mr-${sideOffset} ${alignClasses.left[align]}`,
    right: `left-full ml-${sideOffset} ${alignClasses.right[align]}`,
  };

  if (!open) return null;

  return (
    <>
      <span className="sr-only" id={contentId}>
        {children}
      </span>
      <div
        id={contentId}
        role="tooltip"
        aria-labelledby={tooltipId}
        className={cn(
          'z-50 absolute px-3 py-2 text-sm text-white bg-gray-900 dark:bg-gray-700 rounded-lg shadow-lg whitespace-nowrap',
          positionClasses[side],
          className,
        )}
      >
        {children}
        <div
          className={cn(
            'absolute w-2 h-2 bg-gray-900 dark:bg-gray-700 rotate-45',
            side === 'top' && align === 'center' && 'top-full left-1/2 -translate-x-1/2 -translate-y-1/2',
            side === 'top' && align === 'start' && 'top-full left-2 -translate-y-1/2',
            side === 'top' && align === 'end' && 'top-full right-2 -translate-y-1/2',
            side === 'bottom' && align === 'center' && 'bottom-full left-1/2 translate-x-1/2 translate-y-1/2',
            side === 'bottom' && align === 'start' && 'bottom-full left-2 translate-y-1/2',
            side === 'bottom' && align === 'end' && 'bottom-full right-2 translate-y-1/2',
            side === 'left' && 'left-full top-1/2 -translate-x-1/2 -translate-y-1/2',
            side === 'right' && 'right-full top-1/2 translate-x-1/2 -translate-y-1/2',
          )}
        />
      </div>
    </>
  );
}
