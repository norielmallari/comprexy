import { mkdir } from 'node:fs/promises';
import { join } from 'node:path';

import { expect, test } from '@playwright/test';

/**
 * Captures one full-page dashboard screenshot per bench conversation. Driven by the bench harness
 * (`bench report --screenshots`) against a live control-api; skipped when nothing set the ids.
 */
const conversationIds = (process.env.COMPREXY_EVIDENCE_CONVERSATION_IDS ?? '')
  .split(',')
  .map((id) => id.trim())
  .filter((id) => id.length > 0);

const outputDir = process.env.COMPREXY_EVIDENCE_OUTPUT_DIR ?? 'playwright-report/evidence';

test.describe('bench evidence screenshots', () => {
  test.skip(
    conversationIds.length === 0,
    'Set COMPREXY_EVIDENCE_CONVERSATION_IDS to capture evidence screenshots.',
  );

  test.beforeAll(async () => {
    await mkdir(outputDir, { recursive: true });
  });

  for (const conversationId of conversationIds) {
    test(`conversation ${conversationId}`, async ({ page }) => {
      await page.goto(`/?conv=${encodeURIComponent(conversationId)}`);

      await expect(
        page.getByRole('heading', { name: 'Comprexy Metrics' }),
      ).toBeVisible();
      await expect(page.getByTestId('metrics-grid')).toBeVisible();
      await expect(page.getByTestId('token-counts-by-turn-chart')).toBeVisible();

      await page.screenshot({
        path: join(outputDir, `conversation-${conversationId}.png`),
        fullPage: true,
      });
    });
  }
});
