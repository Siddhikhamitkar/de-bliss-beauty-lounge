import type { CustomerSummary } from '../api/contracts.ts'
import { Avatar } from '../components/Avatar.tsx'
import { RowActionMenu } from '../components/RowActionMenu.tsx'
import { StatusPill } from '../components/StatusPill.tsx'

interface CustomerListProps {
  customers: CustomerSummary[]
  onOpen: (customer: CustomerSummary) => void
  onToggleStatus: (customer: CustomerSummary) => void
}

export function CustomerList({ customers, onOpen, onToggleStatus }: CustomerListProps) {
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
          <tr
            key={customer.id}
            className="row-clickable"
            onClick={() => onOpen(customer)}
          >
            <th scope="row">
              <button
                type="button"
                className="row-link"
                onClick={event => {
                  event.stopPropagation()
                  onOpen(customer)
                }}
              >
                <span className="staff-cell">
                  <Avatar
                    firstName={customer.firstName}
                    lastName={customer.lastName}
                    color={null}
                  />
                  {customer.firstName} {customer.lastName}
                </span>
              </button>
            </th>
            <td>{customer.phoneNumber ?? '—'}</td>
            <td>{customer.email ?? '—'}</td>
            <td><StatusPill isActive={customer.isActive} /></td>
            <td className="actions">
              <RowActionMenu
                label={`Actions for ${customer.firstName} ${customer.lastName}`}
                actions={[
                  {
                    label: customer.isActive ? 'Deactivate' : 'Activate',
                    onSelect: () => onToggleStatus(customer),
                    isDanger: customer.isActive,
                  },
                ]}
              />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
