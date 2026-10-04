import { useState } from 'react'
import { getService, setServiceStatus } from '../api/catalogue.ts'
import { describeFailure } from '../api/client.ts'
import type { ServiceSummary } from '../api/contracts.ts'
import { DetailDrawer, DrawerHeader, DrawerSection } from '../components/DetailDrawer.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { RowActionMenu } from '../components/RowActionMenu.tsx'
import { StatusPill } from '../components/StatusPill.tsx'
import { formatDate, formatDuration, formatPrice } from '../format.ts'
import { useAsync } from '../useAsync.ts'

interface ServiceDrawerProps {
  service: ServiceSummary
  onClose: () => void
  onEdit: (serviceId: string) => void
  onChanged: () => void
}

/**
 * The service record as a right-hand drawer over the still-visible list. The
 * header paints immediately from the row summary; the full record and the
 * assigned team load underneath.
 */
export function ServiceDrawer({ service, onClose, onEdit, onChanged }: ServiceDrawerProps) {
  const details = useAsync(() => getService(service.id), [service.id])
  const [actionFailure, setActionFailure] = useState<string>()

  const isActive = details.data?.isActive ?? service.isActive

  function toggleStatus() {
    setActionFailure(undefined)

    setServiceStatus(service.id, !isActive)
      .then(() => {
        details.reload()
        onChanged()
      })
      .catch((error: unknown) => setActionFailure(describeFailure(error)))
  }

  const detailsData = details.data

  return (
    <DetailDrawer label={`Service ${service.name}`} onClose={onClose}>
      <DrawerHeader
        title={service.name}
        subtitle={
          <>
            <span>{service.categoryName ?? 'Uncategorised'}</span>
            <StatusPill isActive={isActive} />
          </>
        }
        actions={
          <button type="button" className="button" onClick={() => onEdit(service.id)}>
            Edit
          </button>
        }
        menu={
          <RowActionMenu
            label="More service actions"
            actions={[
              {
                label: isActive ? 'Deactivate' : 'Activate',
                onSelect: toggleStatus,
                isDanger: isActive,
              },
            ]}
          />
        }
        onClose={onClose}
      />

      {actionFailure !== undefined && (
        <div className="drawer-section">
          <p className="banner" role="alert">{actionFailure}</p>
        </div>
      )}

      {details.failure !== undefined ? (
        <div className="drawer-section">
          <ErrorState message={details.failure} onRetry={details.reload} />
        </div>
      ) : detailsData === undefined ? (
        <div className="drawer-section">
          <LoadingState label="Loading service…" />
        </div>
      ) : (
        <>
          <DrawerSection title="Overview">
            <dl className="detail-grid">
              <dt>Category</dt>
              <dd>
                {detailsData.categoryName ?? 'Uncategorised'}
                {detailsData.categoryId !== null && !detailsData.isCategoryActive && ' (inactive)'}
              </dd>
              <dt>Duration</dt>
              <dd>{formatDuration(detailsData.durationMinutes)}</dd>
              <dt>Price</dt>
              <dd>{formatPrice(detailsData.price)}</dd>
              <dt>Status</dt>
              <dd><StatusPill isActive={detailsData.isActive} /></dd>
              <dt>Online booking</dt>
              <dd>{detailsData.isOnlineBookable ? 'Yes' : 'No'}</dd>
              <dt>Bookable</dt>
              <dd>
                {detailsData.isBookable ? 'Yes' : 'No'}
                {detailsData.isActive && !detailsData.isBookable && (
                  <span className="row-note">
                    Not bookable: the category is inactive or no eligible staff member is active.
                  </span>
                )}
              </dd>
              <dt>Created</dt>
              <dd>{formatDate(detailsData.createdAt)}</dd>
              <dt>Updated</dt>
              <dd>{detailsData.updatedAt === null ? '—' : formatDate(detailsData.updatedAt)}</dd>
            </dl>
          </DrawerSection>

          <DrawerSection title="Description">
            {detailsData.description === null || detailsData.description === '' ? (
              <p className="empty">No description yet.</p>
            ) : (
              <p>{detailsData.description}</p>
            )}
          </DrawerSection>

          <DrawerSection title="Assigned staff">
            {detailsData.eligibleStaff.length === 0 ? (
              <p className="empty">No staff can perform this service yet.</p>
            ) : (
              <ul className="drawer-list">
                {detailsData.eligibleStaff.map(member => (
                  <li key={member.staffId}>
                    {member.firstName} {member.lastName}
                    {member.jobTitle === null ? '' : ` · ${member.jobTitle}`}
                    {member.isActive ? '' : ' (inactive)'}
                  </li>
                ))}
              </ul>
            )}
          </DrawerSection>
        </>
      )}
    </DetailDrawer>
  )
}
