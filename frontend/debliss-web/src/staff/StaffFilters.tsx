import type { FormEvent } from 'react'
import type { StaffStatusFilter } from '../api/contracts.ts'

interface StaffFiltersProps {
  search: string
  onSearchChange: (value: string) => void
  status: StaffStatusFilter
  onStatusChange: (value: StaffStatusFilter) => void
  /** Applies the typed search term and resets to the first page. */
  onApply: () => void
}

export function StaffFilters({
  search,
  onSearchChange,
  status,
  onStatusChange,
  onApply,
}: StaffFiltersProps) {
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
          placeholder="Name, role, or email"
          value={search}
          onChange={event => onSearchChange(event.target.value)}
        />
      </label>

      <label className="field">
        <span>Status</span>
        <select
          name="status"
          value={status}
          onChange={event => onStatusChange(event.target.value as StaffStatusFilter)}
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
