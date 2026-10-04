import { useState } from 'react'
import type { KeyboardEvent, ReactNode } from 'react'
import { getService, setServiceStatus } from '../api/catalogue.ts'
import { describeFailure } from '../api/client.ts'
import type { Service, ServiceSummary } from '../api/contracts.ts'
import { DetailDrawer, DrawerHeader } from '../components/DetailDrawer.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { RowActionMenu } from '../components/RowActionMenu.tsx'
import { StatusPill } from '../components/StatusPill.tsx'
import { formatDate, formatDuration, formatPrice } from '../format.ts'
import { useAsync } from '../useAsync.ts'

type ServiceTabId = 'overview' | 'staff' | 'details'

const TABS: { id: ServiceTabId; label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'staff', label: 'Assigned staff' },
  { id: 'details', label: 'Service details' },
]

interface ServiceDrawerProps {
  service: ServiceSummary
  onClose: () => void
  onEdit: (serviceId: string) => void
  onChanged: () => void
}

/**
 * The service record as a tabbed workspace inside a right-hand drawer. The
 * header paints immediately from the row summary; the full record loads once
 * behind the tabs and each tab renders only its own section.
 */
export function ServiceDrawer({ service, onClose, onEdit, onChanged }: ServiceDrawerProps) {
  const details = useAsync(() => getService(service.id), [service.id])
  const [actionFailure, setActionFailure] = useState<string>()
  const [activeTab, setActiveTab] = useState<ServiceTabId>('overview')

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

  /** Arrow keys move between tabs — the keyboard gesture the tab pattern expects. */
  function handleTabKeys(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
      return
    }

    event.preventDefault()
    const step = event.key === 'ArrowRight' ? 1 : -1
    const index = TABS.findIndex(tab => tab.id === activeTab)
    const next = TABS[(index + step + TABS.length) % TABS.length]
    setActiveTab(next.id)
    document.getElementById(`service-tab-${next.id}`)?.focus()
  }

  function renderDetailsTab(render: (record: Service) => ReactNode) {
    if (details.failure !== undefined) {
      return <ErrorState message={details.failure} onRetry={details.reload} />
    }

    if (details.data === undefined) {
      return <LoadingState label="Loading service…" />
    }

    return render(details.data)
  }

  function renderTab() {
    switch (activeTab) {
      case 'overview':
        return renderDetailsTab(record => (
          <>
            <section className="tab-section" aria-label="Service summary">
              <div className="tab-section-head">
                <h4>Summary</h4>
              </div>
              <div className="stat-grid">
                <div className="stat-tile">
                  <span className="stat-value">{formatDuration(record.durationMinutes)}</span>
                  <span className="stat-label">Duration</span>
                </div>
                <div className="stat-tile">
                  <span className="stat-value">{formatPrice(record.price)}</span>
                  <span className="stat-label">Price</span>
                </div>
                <div className="stat-tile">
                  <span className="stat-value">{record.eligibleStaff.length}</span>
                  <span className="stat-label">Staff</span>
                </div>
                <div className="stat-tile">
                  <span className="stat-value">{record.isBookable ? 'Yes' : 'No'}</span>
                  <span className="stat-label">Bookable</span>
                </div>
              </div>
              {record.isActive && !record.isBookable && (
                <p className="row-note">
                  Not bookable: the category is inactive or no eligible staff member is active.
                </p>
              )}
            </section>

            <section className="tab-section" aria-label="Description">
              <div className="tab-section-head">
                <h4>Description</h4>
              </div>
              {record.description === null || record.description === '' ? (
                <EmptyState>No description yet.</EmptyState>
              ) : (
                <p>{record.description}</p>
              )}
            </section>
          </>
        ))
      case 'staff':
        return renderDetailsTab(record => (
          <section className="tab-section" aria-label="Assigned staff">
            <div className="tab-section-head">
              <h4>Assigned staff</h4>
            </div>
            {record.eligibleStaff.length === 0 ? (
              <EmptyState>No staff can perform this service yet.</EmptyState>
            ) : (
              <ul className="roster-list">
                {record.eligibleStaff.map(member => (
                  <li key={member.staffId} className="roster-row">
                    <span className="roster-name">
                      {member.firstName} {member.lastName}
                    </span>
                    <span className="roster-role">{member.jobTitle ?? '—'}</span>
                    {!member.isActive && <span className="roster-note">inactive</span>}
                  </li>
                ))}
              </ul>
            )}
          </section>
        ))
      case 'details':
        return renderDetailsTab(record => (
          <section className="tab-section" aria-label="Service details">
            <div className="tab-section-head">
              <h4>Service details</h4>
              <button type="button" className="button" onClick={() => onEdit(service.id)}>
                Edit
              </button>
            </div>
            <dl className="kv-grid">
              <dt>Name</dt>
              <dd>{record.name}</dd>
              <dt>Category</dt>
              <dd>
                {record.categoryName ?? 'Uncategorised'}
                {record.categoryId !== null && !record.isCategoryActive && ' (inactive)'}
              </dd>
              <dt>Duration</dt>
              <dd>{formatDuration(record.durationMinutes)}</dd>
              <dt>Price</dt>
              <dd>{formatPrice(record.price)}</dd>
              <dt>Status</dt>
              <dd><StatusPill isActive={record.isActive} /></dd>
              <dt>Online booking</dt>
              <dd>{record.isOnlineBookable ? 'Yes' : 'No'}</dd>
              <dt>Bookable</dt>
              <dd>{record.isBookable ? 'Yes' : 'No'}</dd>
              <dt>Position</dt>
              <dd>{record.sortOrder}</dd>
              <dt>Created</dt>
              <dd>{formatDate(record.createdAt)}</dd>
              <dt>Updated</dt>
              <dd>{record.updatedAt === null ? '—' : formatDate(record.updatedAt)}</dd>
            </dl>
          </section>
        ))
    }
  }

  return (
    <DetailDrawer
      label={`Service ${service.name}`}
      onClose={onClose}
      panelClassName="drawer-workspace"
    >
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
        <p className="banner" role="alert">{actionFailure}</p>
      )}

      <div
        className="drawer-tabs"
        role="tablist"
        aria-label="Service sections"
        onKeyDown={handleTabKeys}
      >
        {TABS.map(tab => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            id={`service-tab-${tab.id}`}
            aria-selected={activeTab === tab.id}
            aria-controls="service-tabpanel"
            tabIndex={activeTab === tab.id ? 0 : -1}
            className="drawer-tab"
            onClick={() => setActiveTab(tab.id)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <div
        className="drawer-tab-content"
        role="tabpanel"
        id="service-tabpanel"
        aria-labelledby={`service-tab-${activeTab}`}
      >
        {renderTab()}
      </div>
    </DetailDrawer>
  )
}
