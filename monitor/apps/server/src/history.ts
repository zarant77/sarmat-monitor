export function parseHistoryPagination(query: { offset?: string; limit?: string }) {
  const offset = Math.max(0, Number.parseInt(query.offset ?? "0", 10) || 0);
  const limit = Math.min(50, Math.max(10, Number.parseInt(query.limit ?? "25", 10) || 25));
  return { offset, limit };
}
