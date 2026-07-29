/**
 * Skeleton loading component.
 *
 * Renders a pulsing gray placeholder.
 */

import { HTMLAttributes, forwardRef } from 'react';

import { cn } from '../../lib/utils';

export interface SkeletonProps extends HTMLAttributes<HTMLDivElement> {
  variant?: 'rectangular' | 'circular' | 'text';
}

const VARIANT_CLASSES = {
  rectangular: 'rounded',
  circular: 'rounded-full',
  text: 'rounded h-4 w-full',
};

export const Skeleton = forwardRef<HTMLDivElement, SkeletonProps>(
  ({ className, variant = 'rectangular', ...props }, ref) => {
    return (
      <div
        ref={ref}
        className={cn(
          'animate-pulse bg-gray-200 dark:bg-gray-700',
          VARIANT_CLASSES[variant],
          className,
        )}
        {...props}
      />
    );
  },
);

Skeleton.displayName = 'Skeleton';
