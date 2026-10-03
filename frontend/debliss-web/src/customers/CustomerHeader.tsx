import type { CustomerProfile } from '../api/contracts.ts'
import { Avatar } from '../components/Avatar.tsx'

interface CustomerHeaderProps {
  customer: CustomerProfile
  onEdit: (customerId: string) => void
  onToggleStatus: () => void
  onBack: () => void
}

/**
 * The strip at the top of the profile: who the customer is, how to reach
 * them, and the actions a receptionist reaches for most.
 */
export function CustomerHeader({ customer, onEdit, onToggleStatus, onBack }: CustomerHeaderProps) {
  return (
    <div className="panel-header">
      <h2>
        <span className="staff-cell">
          <Avatar firstName={customer.firstName} lastName={customer.lastName} color={null} />
          {customer.firstName} {customer.lastName}
        </span>
      </h2>
      <div className="actions">
        <button type="button" className="button" onClick={() => onEdit(customer.id)}>
          Edit customer
        </button>
        <button type="button" className="button" onClick={onToggleStatus}>
          {customer.isActive ? 'Deactivate' : 'Activate'}
        </button>
        <button type="button" className="button" onClick={onBack}>
          Back to customers
        </button>
      </div>
    </div>
  )
}
