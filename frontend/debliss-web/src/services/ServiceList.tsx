import type { ServiceSummary } from '../api/contracts.ts'
import { RowActionMenu } from '../components/RowActionMenu.tsx'
import { StatusPill } from '../components/StatusPill.tsx'
import { formatDuration, formatPrice } from '../format.ts'

interface ServiceListProps {
  services: ServiceSummary[]
  onOpen: (service: ServiceSummary) => void
  onToggleStatus: (service: ServiceSummary) => void
}

export function ServiceList({ services, onOpen, onToggleStatus }: ServiceListProps) {
  return (
    <table className="grid">
      <thead>
        <tr>
          <th scope="col">Service</th>
          <th scope="col">Category</th>
          <th scope="col">Duration</th>
          <th scope="col" className="numeric">Price</th>
          <th scope="col" className="numeric">Staff</th>
          <th scope="col">Status</th>
          <th scope="col">Online</th>
          <th scope="col">Actions</th>
        </tr>
      </thead>
      <tbody>
        {services.map(service => {
          const isBundle = service.itemKind === 'Bundle'
          // An active offering that cannot actually be booked is the case worth
          // flagging: its category went inactive, or — for a bundle — one of its
          // services did, or everybody who could perform it left. An inactive one
          // is simply switched off.
          const stuck = service.isActive && !service.isBookable

          return (
            <tr
              key={service.id}
              className={stuck ? 'row-clickable row-warning' : 'row-clickable'}
              onClick={() => onOpen(service)}
            >
              <th scope="row">
                <button
                  type="button"
                  className="row-link"
                  onClick={event => {
                    event.stopPropagation()
                    onOpen(service)
                  }}
                >
                  {service.name}
                  {isBundle && (
                    <span className="kind-note">
                      Bundle · {service.serviceCount} services
                    </span>
                  )}
                  {stuck && <span className="row-note">not bookable</span>}
                </button>
              </th>
              <td>{service.categoryName ?? '—'}</td>
              <td>{formatDuration(service.durationMinutes)}</td>
              <td className="numeric">
                {formatPrice(service.price)}
                {isBundle && service.standardTotalPrice !== null && (
                  <span className="was-price">{formatPrice(service.standardTotalPrice)}</span>
                )}
              </td>
              {/* A bundle has no staff of its own: each component is assigned at
                  booking time, and it is never offered online. */}
              <td className="numeric">{isBundle ? '—' : service.eligibleStaffCount}</td>
              <td><StatusPill isActive={service.isActive} /></td>
              <td>{isBundle ? '—' : service.isOnlineBookable ? 'Yes' : 'No'}</td>
              <td className="actions">
                <RowActionMenu
                  label={`Actions for ${service.name}`}
                  actions={[
                    {
                      label: service.isActive ? 'Deactivate' : 'Activate',
                      onSelect: () => onToggleStatus(service),
                      isDanger: service.isActive,
                    },
                  ]}
                />
              </td>
            </tr>
          )
        })}
      </tbody>
    </table>
  )
}
