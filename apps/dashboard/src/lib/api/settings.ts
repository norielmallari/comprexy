/**
 * Operator settings API — GET/PUT /v1/comprexy/settings
 */

import { apiFetch, apiGet, apiPut } from './client';
import type {
  OperatorMutableSettingsDto,
  OperatorSettingsPutRequestDto,
  OperatorSettingsResponseDto,
} from '@/types/api';

export async function getOperatorSettings(): Promise<OperatorSettingsResponseDto> {
  return apiGet<OperatorSettingsResponseDto>('/v1/comprexy/settings');
}

export async function putOperatorSettings(
  body: OperatorSettingsPutRequestDto,
): Promise<OperatorSettingsResponseDto> {
  return apiPut<OperatorSettingsResponseDto>('/v1/comprexy/settings', body);
}

/**
 * PUT with If-Match revision header (optimistic concurrency).
 */
export async function putOperatorSettingsWithEtag(
  revision: number,
  settings: OperatorMutableSettingsDto,
): Promise<OperatorSettingsResponseDto> {
  return apiFetch<OperatorSettingsResponseDto>('/v1/comprexy/settings', {
    method: 'PUT',
    headers: {
      'If-Match': `"${revision}"`,
    },
    body: JSON.stringify({ revision, settings } satisfies OperatorSettingsPutRequestDto),
  });
}
