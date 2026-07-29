import { render, screen, fireEvent } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { Select } from '@/components/ui/select';

describe('Select', () => {
  const options = [
    { value: '1', label: 'Option 1' },
    { value: '2', label: 'Option 2' },
    { value: '3', label: 'Option 3' },
  ];

  it('renders with placeholder', () => {
    render(<Select options={options} placeholder="Choose..." />);
    const select = screen.getByRole('combobox');
    expect(select).toBeInTheDocument();
    expect(screen.getByText('Choose...')).toBeInTheDocument();
  });

  it('renders options', () => {
    render(<Select options={options} />);
    options.forEach((option) => {
      expect(screen.getByText(option.label)).toBeInTheDocument();
    });
  });

  it('renders without placeholder when not provided', () => {
    render(<Select options={options} />);
    expect(screen.queryByText('Choose...')).not.toBeInTheDocument();
  });

  it('renders with label', () => {
    render(<Select options={options} label="Choose an option" />);
    expect(screen.getByText('Choose an option')).toBeInTheDocument();
  });

  it('renders without label when not provided', () => {
    render(<Select options={options} />);
    expect(screen.queryByText('Choose an option')).not.toBeInTheDocument();
  });

  it('calls onChange when option selected', async () => {
    const handleChange = vi.fn();
    const user = userEvent.setup();
    render(<Select options={options} onChange={handleChange} />);
    const select = screen.getByRole('combobox');
    await user.selectOptions(select, '2');
    expect(handleChange).toHaveBeenCalledWith('2');
  });

  it('renders with className', () => {
    render(<Select options={options} className="custom-select" />);
    const select = screen.getByRole('combobox');
    expect(select.className).toContain('custom-select');
  });

  it('forwards ref to the select element', () => {
    const ref = { current: null as HTMLSelectElement | null };
    render(<Select options={options} ref={ref} />);
    expect(ref.current).toBeInstanceOf(HTMLSelectElement);
  });

  it('renders controlled value', () => {
    render(<Select options={options} value="2" />);
    const select = screen.getByRole('combobox') as HTMLSelectElement;
    expect(select.value).toBe('2');
  });

  it('renders disabled select', () => {
    render(<Select options={options} disabled />);
    const select = screen.getByRole('combobox');
    expect(select).toBeDisabled();
  });

  it('renders with required attribute', () => {
    render(<Select options={options} required />);
    const select = screen.getByRole('combobox');
    expect(select).toHaveAttribute('required');
  });

  it('renders select inside a relative container', () => {
    render(<Select options={options} />);
    const container = screen.getByRole('combobox').parentElement;
    expect(container?.className).toContain('relative');
  });

  it('renders select with correct base styles', () => {
    render(<Select options={options} />);
    const select = screen.getByRole('combobox');
    expect(select.className).toContain('appearance-none');
    expect(select.className).toContain('bg-white');
    expect(select.className).toContain('border');
    expect(select.className).toContain('rounded-md');
    expect(select.className).toContain('px-3');
    expect(select.className).toContain('py-2');
    expect(select.className).toContain('text-sm');
    expect(select.className).toContain('cursor-pointer');
  });

  it('renders dark mode styles', () => {
    render(<Select options={options} />);
    const select = screen.getByRole('combobox');
    expect(select.className).toContain('dark:bg-gray-800');
    expect(select.className).toContain('dark:border-gray-600');
    expect(select.className).toContain('dark:text-gray-100');
  });

  it('renders focus styles', () => {
    render(<Select options={options} />);
    const select = screen.getByRole('combobox');
    expect(select.className).toContain('focus:outline-none');
    expect(select.className).toContain('focus:ring-2');
    expect(select.className).toContain('focus:ring-blue-500');
  });
});
