import { useState } from 'react'
import type { KeyboardEvent, ReactNode } from 'react'
import { getBundle, setBundleStatus } from '../api/catalogue.ts'
import { describeFailure } from '../api/client.ts'
import type { Bundle, ServiceSummary } from '../api/contracts.ts'
import { DetailDrawer, DrawerHeader } from '../components/DetailDrawer.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { RowActionMenu } from '../components/RowActionMenu.tsx'
import { StatusPill } from '../components/StatusPill.tsx'
import { formatDate, formatDuration, formatPrice } from '../format.ts'
import { useAsync } from '../useAsync.ts'

type BundleTabId = 'overview' | 'components' | 'details'

const TABS: { id: BundleTabId; label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'components', label: 'Services' },
  { id: 'details', label: 'Bundle details' },
]

interface BundleDrawerProps {
  bundle: ServiceSummary
  onClose: () => void
  onEdit: (bundleId: string) => void
  onChanged: () => void
}

/**
 * The bundle record as a tabbed workspace inside a right-hand drawer, matching
 * the service drawer so the menu reads as one list. The header paints from the
 * row summary; the full record loads once behind the tabs.
 */
export function BundleDrawer({ bundle, onClose, onEdit, onChanged }: BundleDrawerProps) {
  const details = useAsync(() => getBundle(bundle.id), [bundle.id])
  const [actionFailure, setActionFailure] = useState<string>()
  const [activeTab, setActiveTab] = useState<BundleTabId>('overview')

  const isActive = details.data?.isActive ?? bundle.isActive

  function toggleStatus() {
    setActionFailure(undefined)

    setBundleStatus(bundle.id, !isActive)
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
    document.getElementById(`bundle-tab-${next.id}`)?.focus()
  }

  function renderDetailsTab(render: (record: Bundle) => ReactNode) {
    if (details.failure !== undefined) {
      return <ErrorState message={details.failure} onRetry={details.reload} />
    }

    if (details.data === undefined) {
      return <LoadingState label="Loading bundle…" />
    }

    return render(details.data)
  }

  function renderTab() {
    switch (activeTab) {
      case 'overview':
        return renderDetailsTab(record => (
          <>
            <section className="tab-section" aria-label="Bundle summary">
              <div className="tab-section-head">
                <h4>Summary</h4>
              </div>
              <div className="stat-grid">
                <div className="stat-tile">
                  <span className="stat-value">{record.items.length}</span>
                  <span className="stat-label">Services</span>
                </div>
                <div className="stat-tile">
                  <span className="stat-value">{formatDuration(record.durationMinutes)}</span>
                  <span className="stat-label">Duration</span>
                </div>
                <div className="stat-tile">
                  <span className="stat-value">{formatPrice(record.price)}</span>
                  <span className="stat-label">Bundle price</span>
                </div>
                <div className="stat-tile">
                  <span className="stat-value">{formatPrice(record.savingsAmount)}</span>
                  <span className="stat-label">Saving</span>
                </div>
              </div>
              {record.isActive && !record.isBookable && (
                <p className="row-note">
                  Not bookable: the category is inactive, or one of the services is no longer
                  active and bookable.
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
      case 'components':
        return renderDetailsTab(record => (
          <section className="tab-section" aria-label="Bundle services">
            <div className="tab-section-head">
              <h4>Services, in delivery order</h4>
            </div>
            <ol className="roster-list">
              {record.items.map(item => (
                <li key={item.serviceId} className="roster-row">
                  <span className="roster-name">{item.name}</span>
                  <span className="roster-role">
                    {formatDuration(item.durationMinutes)} · {formatPrice(item.price)}
                  </span>
                  {!item.isActive && <span className="roster-note">inactive</span>}
                </li>
              ))}
            </ol>
            <div className="stat-tile stat-tile-wide">
              <span className="stat-label">Bought separately</span>
              <span className="stat-value-date">{formatPrice(record.standardTotalPrice)}</span>
            </div>
          </section>
        ))
      case 'details':
        return renderDetailsTab(record => (
          <section className="tab-section" aria-label="Bundle details">
            <div className="tab-section-head">
              <h4>Bundle details</h4>
              <button type="button" className="button" onClick={() => onEdit(bundle.id)}>
                Edit
              </button>
            </div>
            <dl className="kv-grid">
              <dt>Name</dt>
              <dd>{record.name}</dd>
              <dt>Category</dt>
              <dd>
                {record.categoryName ?? '—'}
                {record.categoryId !== null && !record.isCategoryActive && ' (inactive)'}
              </dd>
              <dt>Services</dt>
              <dd>{record.items.length}</dd>
              <dt>Duration</dt>
              <dd>{formatDuration(record.durationMinutes)}</dd>
              <dt>Bundle price</dt>
              <dd>{formatPrice(record.price)}</dd>
              <dt>Bought separately</dt>
              <dd>{formatPrice(record.standardTotalPrice)}</dd>
              <dt>Saving</dt>
              <dd>{formatPrice(record.savingsAmount)}</dd>
              <dt>Status</dt>
              <dd><StatusPill isActive={record.isActive} /></dd>
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
      label={`Bundle ${bundle.name}`}
      onClose={onClose}
      panelClassName="drawer-workspace"
    >
      <DrawerHeader
        title={bundle.name}
        subtitle={
          <>
            <span className="pill">Bundle</span>
            <span>{bundle.categoryName ?? 'Uncategorised'}</span>
            <StatusPill isActive={isActive} />
          </>
        }
        actions={
          <button type="button" className="button" onClick={() => onEdit(bundle.id)}>
            Edit
          </button>
        }
        menu={
          <RowActionMenu
            label="More bundle actions"
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
        aria-label="Bundle sections"
        onKeyDown={handleTabKeys}
      >
        {TABS.map(tab => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            id={`bundle-tab-${tab.id}`}
            aria-selected={activeTab === tab.id}
            aria-controls="bundle-tabpanel"
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
        id="bundle-tabpanel"
        aria-labelledby={`bundle-tab-${activeTab}`}
      >
        {renderTab()}
      </div>
    </DetailDrawer>
  )
}
