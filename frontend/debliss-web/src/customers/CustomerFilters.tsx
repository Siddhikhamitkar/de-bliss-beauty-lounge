import type { FormEvent } from 'react'
import type { CustomerStatusFilter } from '../api/contracts.ts'

interface CustomerFiltersProps {
  search: string
  onSearchChange: (value: string) => void
  status: CustomerStatusFilter
  onStatusChange: (value: CustomerStatusFilter) => void
  /** Applies the typed search term and resets to the first page. */
  onApply: () => void
}

export function CustomerFilters({
  search,
  onSearchChange,
  status,
  onStatusChange,
  onApply,
}: CustomerFiltersProps) {
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    onApply()
  }

  return (
    <form className="filters" onSubmit={submit}>
      <label className="field">
        <span>Search</span>
        <input
          type="search"
          name="search"
          placeholder="Name, mobile, or email"
          value={search}
          onChange={event => onSearchChange(event.target.value)}
        />
      </label>

      <label className="field">
        <span>Status</span>
        <select
          name="status"
          value={status}
          onChange={event => onStatusChange(event.target.value as CustomerStatusFilter)}
        >
          <option value="All">All</option>
          <option value="Active">Active</option>
          <option value="Inactive">Inactive</option>
        </select>
      </label>

      <button type="submit" className="button">Apply</button>
    </form>
  )
}
