import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ConversationIoCards } from '@/components/metrics/conversation-io-cards';
import {
  CATALOG_MODELS,
  storeSelectorStub,
} from '@/__tests__/helpers/cost-model-mocks';

vi.mock('@/lib/utils', () => ({
  formatNumber: (n: number) => n.toLocaleString('en-US'),
}));

vi.mock('@/lib/queries/use-cost-models', () => ({
  useCostModels: vi.fn(),
}));

vi.mock('@/lib/store/dashboard-store', () => ({
  useDashboardStore: vi.fn(),
}));

import { useCostModels } from '@/lib/queries/use-cost-models';
import { useDashboardStore } from '@/lib/store/dashboard-store';

const mockUseCostModels = useCostModels as unknown as ReturnType<typeof vi.fn>;
const mockUseDashboardStore = useDashboardStore as unknown as ReturnType<typeof vi.fn>;

describe('ConversationIoCards', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseCostModels.mockReturnValue({
      data: CATALOG_MODELS,
      isLoading: false,
      isError: false,
    });
    mockUseDashboardStore.mockImplementation(storeSelectorStub('local'));
  });

  it('renders Raw, Input, and Output regions with formatted values', () => {
    render(
      <ConversationIoCards
        totalRawInputTokensEstimated={12000}
        totalCompressedPromptTokens={8000}
        totalCompletionTokens={600}
      />,
    );

    expect(
      screen.getByRole('region', { name: 'Raw input tokens' }),
    ).toHaveTextContent('12,000');
    expect(
      screen.getByRole('region', { name: 'Input tokens' }),
    ).toHaveTextContent('8,000');
    expect(
      screen.getByRole('region', { name: 'Output tokens' }),
    ).toHaveTextContent('600');
  });

  it('hides `$` overlays when Local (zero-rate) is selected', () => {
    render(
      <ConversationIoCards
        totalRawInputTokensEstimated={12000}
        totalCompressedPromptTokens={8000}
        totalCompletionTokens={600}
      />,
    );

    expect(screen.queryByLabelText(/Estimated cost/)).not.toBeInTheDocument();
    expect(screen.getByTestId('conversation-io-cards').textContent).not.toMatch(/\$/);
  });

  it('shows `$` overlays when Sonnet is selected', () => {
    mockUseDashboardStore.mockImplementation(storeSelectorStub('claude-sonnet-5'));

    render(
      <ConversationIoCards
        totalRawInputTokensEstimated={12000}
        totalCompressedPromptTokens={8000}
        totalCompletionTokens={600}
      />,
    );

    expect(screen.getByLabelText('Estimated cost $0.0360')).toBeInTheDocument();
    expect(screen.getByLabelText('Estimated cost $0.0240')).toBeInTheDocument();
    expect(screen.getByLabelText('Estimated cost $0.0090')).toBeInTheDocument();
  });

  it('exposes root data-testid conversation-io-cards', () => {
    render(
      <ConversationIoCards
        totalRawInputTokensEstimated={12000}
        totalCompressedPromptTokens={8000}
        totalCompletionTokens={600}
      />,
    );

    expect(screen.getByTestId('conversation-io-cards')).toBeInTheDocument();
  });

  it('shows an em dash when each token prop is null', () => {
    render(
      <ConversationIoCards
        totalRawInputTokensEstimated={null}
        totalCompressedPromptTokens={null}
        totalCompletionTokens={null}
      />,
    );

    expect(
      screen.getByRole('region', { name: 'Raw input tokens' }),
    ).toHaveTextContent('—');
    expect(
      screen.getByRole('region', { name: 'Input tokens' }),
    ).toHaveTextContent('—');
    expect(
      screen.getByRole('region', { name: 'Output tokens' }),
    ).toHaveTextContent('—');
  });

  it('omits Raw region when totalRawInputTokensEstimated prop is omitted', () => {
    render(
      <ConversationIoCards
        totalCompressedPromptTokens={8000}
        totalCompletionTokens={600}
      />,
    );

    expect(
      screen.queryByRole('region', { name: 'Raw input tokens' }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('region', { name: 'Input tokens' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('region', { name: 'Output tokens' }),
    ).toBeInTheDocument();
  });

  it('shows Raw region with em dash when totalRawInputTokensEstimated is null', () => {
    render(
      <ConversationIoCards
        totalRawInputTokensEstimated={null}
        totalCompressedPromptTokens={8000}
        totalCompletionTokens={600}
      />,
    );

    expect(
      screen.getByRole('region', { name: 'Raw input tokens' }),
    ).toHaveTextContent('—');
  });

  it('renders tokens unit labels on Input and Output cards', () => {
    render(
      <ConversationIoCards
        totalRawInputTokensEstimated={100}
        totalCompressedPromptTokens={50}
        totalCompletionTokens={10}
      />,
    );

    const input = screen.getByRole('region', { name: 'Input tokens' });
    const output = screen.getByRole('region', { name: 'Output tokens' });
    expect(input).toHaveTextContent('tokens');
    expect(output).toHaveTextContent('tokens');
  });

  it('renders Virtual Tools channel when totalVirtualToolsTokensSaved is provided', () => {
    render(
      <ConversationIoCards
        totalRawInputTokensEstimated={12000}
        totalCompressedPromptTokens={8000}
        totalCompletionTokens={600}
        totalVirtualToolsTokensSaved={1000}
      />,
    );

    const vt = screen.getByRole('region', { name: 'Virtual Tools channel' });
    expect(vt).toHaveTextContent('1,000');
    expect(vt).toHaveTextContent('not tools-only');
    expect(vt).toHaveTextContent('may be negative');
    expect(
      screen.getByRole('region', { name: 'Raw input tokens' }),
    ).toHaveTextContent('12,000');
  });

  it('omits Virtual Tools channel when totalVirtualToolsTokensSaved is undefined', () => {
    render(
      <ConversationIoCards
        totalRawInputTokensEstimated={12000}
        totalCompressedPromptTokens={8000}
        totalCompletionTokens={600}
      />,
    );

    expect(
      screen.queryByRole('region', { name: 'Virtual Tools channel' }),
    ).not.toBeInTheDocument();
  });

  it('shows Virtual Tools channel with em dash when totalVirtualToolsTokensSaved is null', () => {
    render(
      <ConversationIoCards
        totalRawInputTokensEstimated={12000}
        totalCompressedPromptTokens={8000}
        totalCompletionTokens={600}
        totalVirtualToolsTokensSaved={null}
      />,
    );

    expect(
      screen.getByRole('region', { name: 'Virtual Tools channel' }),
    ).toHaveTextContent('—');
  });
});
