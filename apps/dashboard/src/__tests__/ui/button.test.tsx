import { render, screen } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { Button } from '@/components/ui/button';

describe('Button', () => {
  it('renders with default props (variant=primary, size=md)', () => {
    render(<Button>Click me</Button>);
    const button = screen.getByRole('button', { name: /click me/i });
    expect(button).toBeInTheDocument();
    expect(button.className).toContain('inline-flex');
    expect(button.className).toContain('items-center');
    expect(button.className).toContain('justify-center');
    expect(button.className).toContain('font-medium');
    expect(button.className).toContain('transition-colors');
    expect(button.className).toContain('bg-blue-600');
    expect(button.className).toContain('text-white');
    expect(button.className).toContain('px-4');
    expect(button.className).toContain('py-2');
  });

  it('renders with variant=primary', () => {
    render(<Button variant="primary">Primary</Button>);
    const button = screen.getByRole('button', { name: /primary/i });
    expect(button.className).toContain('bg-blue-600');
    expect(button.className).toContain('text-white');
  });

  it('renders with variant=secondary', () => {
    render(<Button variant="secondary">Secondary</Button>);
    const button = screen.getByRole('button', { name: /secondary/i });
    expect(button.className).toContain('bg-gray-200');
    expect(button.className).toContain('text-gray-900');
  });

  it('renders with variant=ghost', () => {
    render(<Button variant="ghost">Ghost</Button>);
    const button = screen.getByRole('button', { name: /ghost/i });
    expect(button.className).toContain('bg-transparent');
    expect(button.className).toContain('text-gray-700');
  });

  it('renders with size=sm', () => {
    render(<Button size="sm">Small</Button>);
    const button = screen.getByRole('button', { name: /small/i });
    expect(button.className).toContain('px-2');
    expect(button.className).toContain('py-1');
    expect(button.className).toContain('text-sm');
  });

  it('renders with size=md', () => {
    render(<Button size="md">Medium</Button>);
    const button = screen.getByRole('button', { name: /medium/i });
    expect(button.className).toContain('px-4');
    expect(button.className).toContain('py-2');
    expect(button.className).toContain('text-sm');
    expect(button.className).toContain('rounded-md');
  });

  it('renders with size=lg', () => {
    render(<Button size="lg">Large</Button>);
    const button = screen.getByRole('button', { name: /large/i });
    expect(button.className).toContain('px-6');
    expect(button.className).toContain('py-3');
    expect(button.className).toContain('text-base');
  });

  it('renders with size=icon', () => {
    render(<Button size="icon" aria-label="Icon button">Icon</Button>);
    const button = screen.getByRole('button', { name: /icon button/i });
    expect(button.className).toContain('p-2');
  });

  it('renders children content', () => {
    render(<Button>Save Changes</Button>);
    expect(screen.getByRole('button', { name: /save changes/i })).toBeInTheDocument();
  });

  it('renders with custom className', () => {
    render(<Button className="custom-class">Custom</Button>);
    const button = screen.getByRole('button', { name: /custom/i });
    expect(button.className).toContain('custom-class');
  });

  it('forwards ref to the button element', () => {
    const ref = { current: null as HTMLButtonElement | null };
    render(<Button ref={ref}>Ref Test</Button>);
    expect(ref.current).toBeInstanceOf(HTMLButtonElement);
    expect(screen.getByRole('button', { name: /ref test/i })).toBe(ref.current);
  });

  it('calls onClick handler when clicked', async () => {
    const handleClick = vi.fn();
    const user = userEvent.setup();
    render(<Button onClick={handleClick}>Click</Button>);
    await user.click(screen.getByRole('button', { name: /click/i }));
    expect(handleClick).toHaveBeenCalledTimes(1);
  });

  it('passes through additional HTML attributes', () => {
    render(
      <Button type="submit" disabled data-testid="submit-btn">
        Submit
      </Button>,
    );
    const button = screen.getByRole('button', { name: /submit/i });
    expect(button).toHaveAttribute('type', 'submit');
    expect(button).toBeDisabled();
  });

  it('renders with correct focus styles', () => {
    render(<Button>Focus</Button>);
    const button = screen.getByRole('button', { name: /focus/i });
    expect(button.className).toContain('focus:outline-none');
    expect(button.className).toContain('focus:ring-2');
    expect(button.className).toContain('focus:ring-blue-500');
  });

  it('renders with disabled state styles', () => {
    render(<Button disabled>Disabled</Button>);
    const button = screen.getByRole('button', { name: /disabled/i });
    expect(button).toBeDisabled();
    expect(button.className).toContain('disabled:opacity-50');
    expect(button.className).toContain('disabled:cursor-not-allowed');
  });
});
