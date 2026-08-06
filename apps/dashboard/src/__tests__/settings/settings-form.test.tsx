import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { SettingsForm } from '@/components/settings/settings-form';
import type { OperatorSettingsResponseDto } from '@/types/api';
import { OptimizationModeValues } from '@/types/api';

vi.mock('@/lib/api/settings', () => ({
  putOperatorSettingsWithEtag: vi.fn(),
}));

import { putOperatorSettingsWithEtag } from '@/lib/api/settings';

const mockPut = putOperatorSettingsWithEtag as unknown as ReturnType<typeof vi.fn>;

function makeInitial(
  overrides: Partial<OperatorSettingsResponseDto> = {},
): OperatorSettingsResponseDto {
  return {
    revision: 3,
    updatedAt: '2026-01-15T12:00:00.000Z',
    settings: {
      proxy: {
        passThrough: false,
        optimizationMode: OptimizationModeValues.Full,
        stripReasoningContent: false,
      },
      metrics: { enabled: true, promptTokenBasis: 1 },
    },
    ...overrides,
  };
}

describe('SettingsForm', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows PassThrough wins banner when passThrough is true', () => {
    render(
      <SettingsForm
        initial={makeInitial({
          settings: {
            proxy: {
              passThrough: true,
              optimizationMode: OptimizationModeValues.MonitorOnly,
            },
          },
        })}
        onSaved={vi.fn()}
      />,
    );

    expect(screen.getByRole('status')).toHaveTextContent(/PassThrough wins/i);
    expect(screen.getByTestId('passthrough-wins-banner')).toBeInTheDocument();
  });

  it('hides PassThrough banner when passThrough is false', () => {
    render(<SettingsForm initial={makeInitial()} onSaved={vi.fn()} />);

    expect(screen.queryByTestId('passthrough-wins-banner')).not.toBeInTheDocument();
  });

  it('surfaces 409 conflict with currentRevision', async () => {
    mockPut.mockRejectedValue({
      message: 'Conflict',
      statusCode: 409,
      currentRevision: 9,
    });

    render(<SettingsForm initial={makeInitial()} onSaved={vi.fn()} />);

    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/Revision conflict \(409\)/);
      expect(screen.getByRole('alert')).toHaveTextContent('9');
    });
  });

  it('calls onSaved after successful put', async () => {
    const onSaved = vi.fn();
    const next = makeInitial({ revision: 4 });
    mockPut.mockResolvedValue(next);

    render(<SettingsForm initial={makeInitial()} onSaved={onSaved} />);

    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    await waitFor(() => {
      expect(onSaved).toHaveBeenCalledWith(next);
    });
    expect(mockPut).toHaveBeenCalledWith(3, expect.any(Object));
  });
});
