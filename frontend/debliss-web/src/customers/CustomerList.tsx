import type { CustomerSummary } from '../api/contracts.ts'
import { Avatar } from '../components/Avatar.tsx'
import { StatusPill } from '../components/StatusPill.tsx'

interface CustomerListProps {
  customers: CustomerSummary[]
  onView: (customer: CustomerSummary) => void
  onEdit: (customer: CustomerSummary) => void
  onToggleStatus: (customer: CustomerSummary) => void
}

export function CustomerList({ customers, onView, onEdit, onToggleStatus }: CustomerListProps) {
  return (
    <table className="grid">
      <thead>
        <tr>
          <th scope="col">Customer</th>
          <th scope="col">Mobile</th>
          <th scope="col">Email</th>
          <th scope="col">Status</th>
          <th scope="col">Actions</th>
        </tr>
      </thead>
      <tbody>
        {customers.map(customer => (
          <tr key={customer.id}>
            <th scope="row">
              <span className="staff-cell">
                <Avatar
                  firstName={customer.firstName}
                  lastName={customer.lastName}
                  color={null}
                />
                {customer.firstName} {customer.lastName}
              </span>
            </th>
            <td>{customer.phoneNumber ?? '—'}</td>
            <td>{customer.email ?? '—'}</td>
            <td><StatusPill isActive={customer.isActive} /></td>
            <td className="actions">
              <button type="button" className="button" onClick={() => onView(customer)}>
                View
              </button>
              <button type="button" className="button" onClick={() => onEdit(customer)}>
                Edit
              </button>
              <button
                type="button"
                className="button"
                onClick={() => onToggleStatus(customer)}
              >
                {customer.isActive ? 'Deactivate' : 'Activate'}
              </button>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
