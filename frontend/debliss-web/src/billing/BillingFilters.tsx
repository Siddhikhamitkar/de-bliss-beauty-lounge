import type { FormEvent } from 'react'
import { PAYMENT_METHODS, PAYMENT_METHOD_LABELS } from '../api/contracts.ts'
import type { InvoicePaymentMethodFilter, InvoiceStatusFilter } from '../api/contracts.ts'

interface BillingFiltersProps {
  search: string
  onSearchChange: (value: string) => void
  status: InvoiceStatusFilter
  onStatusChange: (value: InvoiceStatusFilter) => void
  paymentMethod: InvoicePaymentMethodFilter
  onPaymentMethodChange: (value: InvoicePaymentMethodFilter) => void
  /** Applies the typed search term and resets to the first page. */
  onApply: () => void
}

export function BillingFilters({
  search,
  onSearchChange,
  status,
  onStatusChange,
  paymentMethod,
  onPaymentMethodChange,
  onApply,
}: BillingFiltersProps) {
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
          placeholder="Invoice number or customer"
          value={search}
          onChange={event => onSearchChange(event.target.value)}
        />
      </label>

      <label className="field">
        <span>Status</span>
        <select
          name="status"
          value={status}
          onChange={event => onStatusChange(event.target.value as InvoiceStatusFilter)}
        >
          <option value="All">All</option>
          <option value="Draft">Draft</option>
          <option value="Paid">Paid</option>
        </select>
      </label>

      <label className="field">
        <span>Payment method</span>
        <select
          name="paymentMethod"
          value={paymentMethod}
          onChange={event =>
            onPaymentMethodChange(event.target.value as InvoicePaymentMethodFilter)
          }
        >
          <option value="All">All</option>
          {PAYMENT_METHODS.map(method => (
            <option key={method} value={method}>{PAYMENT_METHOD_LABELS[method]}</option>
          ))}
        </select>
      </label>

      <button type="submit" className="button">Apply</button>
    </form>
  )
}
