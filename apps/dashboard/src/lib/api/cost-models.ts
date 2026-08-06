/**
 * Cost catalog API — GET /v1/comprexy/cost-models
 */

import { apiGet } from './client';
import type { CostModelDto } from '@/types/api';

export async function listCostModels(): Promise<CostModelDto[]> {
  return apiGet<CostModelDto[]>('/v1/comprexy/cost-models');
}
