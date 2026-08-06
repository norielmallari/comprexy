import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import {
  EffectiveSettingsSnapshot,
  summarizeEffectiveSettingsJson,
} from '@/components/settings/effective-settings-snapshot';

describe('summarizeEffectiveSettingsJson', () => {
  it('returns N/A for null/empty', () => {
    expect(summarizeEffectiveSettingsJson(null).label).toBe('N/A');
    expect(summarizeEffectiveSettingsJson(undefined).hasSnapshot).toBe(false);
    expect(summarizeEffectiveSettingsJson('  ').hasSnapshot).toBe(false);
  });

  it('prefers PassThrough when passThrough is true', () => {
    const summary = summarizeEffectiveSettingsJson(
      JSON.stringify({ v: 1, passThrough: true, optimizationMode: 'monitorOnly' }),
    );
    expect(summary.label).toBe('PassThrough');
    expect(summary.hasSnapshot).toBe(true);
  });

  it('maps optimizationMode Full and MonitorOnly', () => {
    expect(
      summarizeEffectiveSettingsJson(
        JSON.stringify({ v: 1, passThrough: false, optimizationMode: 'full' }),
      ).label,
    ).toBe('Full');
    expect(
      summarizeEffectiveSettingsJson(
        JSON.stringify({ v: 1, passThrough: false, optimizationMode: 1 }),
      ).label,
    ).toBe('MonitorOnly');
  });
});

describe('EffectiveSettingsSnapshot', () => {
  it('shows accessible N/A when snapshot is null', () => {
    render(<EffectiveSettingsSnapshot effectiveSettingsJson={null} />);

    expect(screen.getByLabelText('Effective settings not available')).toHaveTextContent(
      'N/A',
    );
    expect(screen.getByTestId('effective-settings-na')).toHaveTextContent('N/A');
    expect(screen.queryByTestId('effective-settings-json')).not.toBeInTheDocument();
  });

  it('shows accessible N/A when snapshot is undefined', () => {
    render(<EffectiveSettingsSnapshot effectiveSettingsJson={undefined} />);

    expect(
      screen.getByRole('button', { name: 'Effective settings not available' }),
    ).toBeInTheDocument();
    expect(screen.getByTestId('effective-settings-na')).toHaveTextContent('N/A');
  });

  it('shows mode on the card and JSON on hover/focus', async () => {
    const user = userEvent.setup();
    const json = JSON.stringify({
      v: 1,
      passThrough: false,
      optimizationMode: 'monitorOnly',
      softLimitTokens: 64000,
    });
    render(<EffectiveSettingsSnapshot effectiveSettingsJson={json} />);

    const trigger = screen.getByRole('button', {
      name: 'Conversation effective settings, MonitorOnly',
    });
    expect(screen.getByTestId('effective-settings-mode')).toHaveTextContent('MonitorOnly');
    expect(screen.queryByTestId('effective-settings-json')).not.toBeInTheDocument();

    await user.hover(trigger);
    const tip = await screen.findByRole('tooltip');
    expect(tip).toHaveTextContent('"optimizationMode": "monitorOnly"');
    expect(tip.querySelector('[data-testid="effective-settings-json"]')).not.toBeNull();
  });
});
