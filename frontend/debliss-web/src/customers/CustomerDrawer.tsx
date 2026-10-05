import { useState } from 'react'
import type { KeyboardEvent, ReactNode } from 'react'
import { describeFailure } from '../api/client.ts'
import { getCustomer, setCustomerStatus } from '../api/customers.ts'
import type { CustomerProfile, CustomerSummary } from '../api/contracts.ts'
import { Avatar } from '../components/Avatar.tsx'
import { DetailDrawer, DrawerHeader } from '../components/DetailDrawer.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { RowActionMenu } from '../components/RowActionMenu.tsx'
import { StatusPill } from '../components/StatusPill.tsx'
import { useAsync } from '../useAsync.ts'
import { CustomerAppointmentHistory } from './CustomerAppointmentHistory.tsx'
import { CustomerDetails } from './CustomerDetails.tsx'
import { CustomerNotes } from './CustomerNotes.tsx'
import { CustomerOverview } from './CustomerOverview.tsx'
import { CustomerSales } from './CustomerSales.tsx'

type CustomerTabId = 'overview' | 'appointments' | 'sales' | 'details' | 'notes'

const TABS: { id: CustomerTabId; label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'appointments', label: 'Appointments' },
  { id: 'sales', label: 'Sales' },
  { id: 'details', label: 'Customer details' },
  { id: 'notes', label: 'Notes' },
]

interface CustomerDrawerProps {
  customer: CustomerSummary
  onClose: () => void
  onEdit: (customerId: string) => void
  onBookAppointment: (customer: CustomerSummary) => void
  onChanged: () => void
}

/**
 * The customer record as a tabbed workspace inside a right-hand drawer. The
 * header carries identity and actions; each tab renders only its own section,
 * so the panel stays scannable instead of one long column. The profile loads
 * once behind the tabs; appointments and notes fetch on their own.
 */
export function CustomerDrawer({
  customer,
  onClose,
  onEdit,
  onBookAppointment,
  onChanged,
}: CustomerDrawerProps) {
  const profile = useAsync(() => getCustomer(customer.id), [customer.id])
  const [actionFailure, setActionFailure] = useState<string>()
  const [activeTab, setActiveTab] = useState<CustomerTabId>('overview')

  const isActive = profile.data?.isActive ?? customer.isActive

  function toggleStatus() {
    setActionFailure(undefined)

    setCustomerStatus(customer.id, !isActive)
      .then(() => {
        profile.reload()
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
    document.getElementById(`customer-tab-${next.id}`)?.focus()
  }

  function renderProfileTab(render: (profile: CustomerProfile) => ReactNode) {
    if (profile.failure !== undefined) {
      return <ErrorState message={profile.failure} onRetry={profile.reload} />
    }

    if (profile.data === undefined) {
      return <LoadingState label="Loading profile…" />
    }

    return render(profile.data)
  }

  function renderTab() {
    switch (activeTab) {
      case 'overview':
        return renderProfileTab(profileData => (
          <CustomerOverview profile={profileData} onChanged={profile.reload} />
        ))
      case 'appointments':
        return <CustomerAppointmentHistory customerId={customer.id} />
      case 'sales':
        return <CustomerSales customerId={customer.id} />
      case 'details':
        return renderProfileTab(profileData => (
          <CustomerDetails profile={profileData} onEdit={() => onEdit(customer.id)} />
        ))
      case 'notes':
        return <CustomerNotes customerId={customer.id} />
    }
  }

  return (
    <DetailDrawer
      label={`Customer ${customer.firstName} ${customer.lastName}`}
      onClose={onClose}
      panelClassName="drawer-workspace"
    >
      <DrawerHeader
        title={`${customer.firstName} ${customer.lastName}`}
        subtitle={
          <>
            <span>{customer.phoneNumber ?? '—'}</span>
            <StatusPill isActive={isActive} />
          </>
        }
        avatar={
          <Avatar firstName={customer.firstName} lastName={customer.lastName} color={null} />
        }
        actions={
          <>
            <button type="button" className="button" onClick={() => onEdit(customer.id)}>
              Edit
            </button>
            <button
              type="button"
              className="button button-primary"
              onClick={() => onBookAppointment(customer)}
            >
              Book appointment
            </button>
          </>
        }
        menu={
          <RowActionMenu
            label="More customer actions"
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
        aria-label="Customer sections"
        onKeyDown={handleTabKeys}
      >
        {TABS.map(tab => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            id={`customer-tab-${tab.id}`}
            aria-selected={activeTab === tab.id}
            aria-controls="customer-tabpanel"
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
        id="customer-tabpanel"
        aria-labelledby={`customer-tab-${activeTab}`}
      >
        {renderTab()}
      </div>
    </DetailDrawer>
  )
}
