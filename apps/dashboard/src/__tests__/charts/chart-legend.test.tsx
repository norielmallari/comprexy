import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { ChartLegend } from '@/components/charts/chart-legend';
import type { ChartLegendItem } from '@/types/chart';

const defaultItems: ChartLegendItem[] = [
  { label: 'System', color: '#cbd5e0' },
  { label: 'History + tools', color: '#94a3b8' },
  { label: 'Compressed WM', color: '#2d6bc4' },
];

describe('ChartLegend', () => {
  it('renders legend items with color blocks', () => {
    render(<ChartLegend items={defaultItems} />);

    defaultItems.forEach((item) => {
      expect(screen.getByText(item.label)).toBeInTheDocument();
    });
  });

  it('renders label text for each item', () => {
    const { container } = render(<ChartLegend items={defaultItems} />);

    const labelSpans = container.querySelectorAll('span.text-sm');
    expect(labelSpans).toHaveLength(defaultItems.length);

    defaultItems.forEach((item) => {
      expect(screen.getByText(item.label)).toBeInTheDocument();
    });
  });

  it('renders with empty array (no items)', () => {
    const { container } = render(<ChartLegend items={[]} />);

    const flexContainer = container.querySelector('div.flex');
    expect(flexContainer).toBeInTheDocument();
    expect(flexContainer?.children.length).toBe(0);
  });

  it('applies correct background color to each color block', () => {
    const { container } = render(<ChartLegend items={defaultItems} />);

    const colorBlocks = container.querySelectorAll('span.inline-block');
    expect(colorBlocks).toHaveLength(defaultItems.length);

    defaultItems.forEach((item, index) => {
      const block = colorBlocks[index];
      expect(block).toHaveStyle({ backgroundColor: item.color });
    });
  });

  it('renders color blocks with correct Tailwind classes', () => {
    const { container } = render(<ChartLegend items={defaultItems} />);

    const colorBlocks = container.querySelectorAll('span.inline-block');
    colorBlocks.forEach((block) => {
      expect(block.className).toContain('h-3');
      expect(block.className).toContain('w-3');
      expect(block.className).toContain('rounded');
    });
  });

  it('wraps items in flex container with correct classes', () => {
    const { container } = render(<ChartLegend items={defaultItems} />);

    const root = container.querySelector('div.flex.flex-wrap');
    expect(root).toBeInTheDocument();
    expect(root?.className).toContain('justify-center');
    expect(root?.className).toContain('gap-3');
  });

  it('renders each legend item with correct structure', () => {
    const singleItem: ChartLegendItem[] = [{ label: 'Test Item', color: '#ff0000' }];
    const { container } = render(<ChartLegend items={singleItem} />);

    const itemContainers = container.querySelectorAll('div.flex.items-center.gap-2');
    expect(itemContainers).toHaveLength(1);

    const colorBlock = container.querySelector('span.inline-block');
    expect(colorBlock).toHaveStyle({ backgroundColor: '#ff0000' });
    expect(screen.getByText('Test Item')).toBeInTheDocument();
  });

  it('draws outlined items as a dashed border instead of a solid swatch', () => {
    const { container } = render(
      <ChartLegend items={[{ label: 'SoftBudget (IR full)', color: '#64748b', outlined: true }]} />,
    );

    const block = container.querySelector('span.inline-block');
    expect(block).toHaveAttribute('data-outlined', 'true');
    expect(block).toHaveStyle({ border: '1px dashed #64748b' });
    expect(block).not.toHaveStyle({ backgroundColor: '#64748b' });
  });

  it('renders unique keys based on label', () => {
    const itemsWithDuplicateLabels: ChartLegendItem[] = [
      { label: 'Same', color: '#ff0000' },
      { label: 'Same', color: '#00ff00' },
    ];
    const { container } = render(<ChartLegend items={itemsWithDuplicateLabels} />);

    const colorBlocks = container.querySelectorAll('span.inline-block');
    expect(colorBlocks).toHaveLength(2);
  });

  it('applies dark mode text color class', () => {
    const { container } = render(<ChartLegend items={defaultItems} />);

    const labelSpans = container.querySelectorAll('span.text-sm');
    labelSpans.forEach((span) => {
      expect(span.className).toContain('dark:text-gray-400');
    });
  });

  it('renders with many items without crashing', () => {
    const manyItems: ChartLegendItem[] = Array.from({ length: 20 }, (_, i) => ({
      label: `Item ${i}`,
      color: `#${i.toString(16).padStart(6, '0')}`,
    }));

    render(<ChartLegend items={manyItems} />);

    manyItems.forEach((item) => {
      expect(screen.getByText(item.label)).toBeInTheDocument();
    });
  });
});
