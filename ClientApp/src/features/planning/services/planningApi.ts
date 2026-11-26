import { getJson } from '../../../lib/api';
import type { SourceDocumentResponse } from '../shared/types';
export interface DocumentQuery { hubId: string; projectId: string; start: string; end: string; page?: number; pageSize?: number; discipline?: string }
export function getPlanningDocuments(query: DocumentQuery, signal?: AbortSignal) {
  const parameters = new URLSearchParams(Object.entries(query).filter(([, value]) => value !== undefined && value !== '').map(([key, value]) => [key, String(value)]));
  return getJson<SourceDocumentResponse>(`/api/weekly-planning/documents?${parameters}`, signal);
}
