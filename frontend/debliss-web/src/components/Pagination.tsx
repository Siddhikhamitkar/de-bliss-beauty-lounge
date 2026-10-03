import type { PagedResult } from '../api/contracts.ts'

/**
 * Previous/next driven entirely by the paging metadata the API sends, so the
 * browser never has to know the page size or repeat the clamping rules.
 */
export function Pagination<T>({
  result,
  onChange,
}: {
  result: PagedResult<T>
  onChange: (page: number) => void
}) {
  return (
    <div className="pagination">
      <span className="pagination-summary">
        Page {result.page} of {Math.max(result.totalPages, 1)} · {result.totalCount} in total
      </span>
      <span className="pagination-buttons">
        <button
          type="button"
          className="button"
          disabled={!result.hasPreviousPage}
          onClick={() => onChange(result.page - 1)}
        >
          Previous
        </button>
        <button
          type="button"
          className="button"
          disabled={!result.hasNextPage}
          onClick={() => onChange(result.page + 1)}
        >
          Next
        </button>
      </span>
    </div>
  )
}
