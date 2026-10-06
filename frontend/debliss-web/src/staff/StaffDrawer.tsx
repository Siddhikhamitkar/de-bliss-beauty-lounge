import { useState } from 'react'
import type { KeyboardEvent, ReactNode } from 'react'
import { describeFailure } from '../api/client.ts'
import { getStaff, setStaffStatus } from '../api/staff.ts'
import type { StaffProfile, StaffSummary } from '../api/contracts.ts'
import { Avatar } from '../components/Avatar.tsx'
import { DetailDrawer, DrawerHeader } from '../components/DetailDrawer.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { RowActionMenu } from '../components/RowActionMenu.tsx'
import { StatusPill } from '../components/StatusPill.tsx'
import { formatDate } from '../format.ts'
import { useAsync } from '../useAsync.ts'
import { AttendanceSection } from './AttendanceSection.tsx'
import { ServiceAssignment } from './ServiceAssignment.tsx'
import { TimeOffSection } from './TimeOffSection.tsx'
import { WorkingHoursEditor } from './WorkingHoursEditor.tsx'

type StaffTabId = 'overview' | 'services' | 'hours' | 'timeoff' | 'attendance' | 'details'

const TABS: { id: StaffTabId; label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'services', label: 'Services' },
  { id: 'hours', label: 'Working hours' },
  { id: 'timeoff', label: 'Time off' },
  { id: 'attendance', label: 'Attendance' },
  { id: 'details', label: 'Staff details' },
]

interface StaffDrawerProps {
  member: StaffSummary
  onClose: () => void
  onEdit: (staffId: string) => void
  onChanged: () => void
  /** Attendance is owner-only; receptionists keep the operational roster. */
  canViewAttendance?: boolean
}

/**
 * The member's profile as a tabbed workspace inside a right-hand drawer. The
 * header carries identity and actions; the profile loads once behind the tabs
 * and the roster panels fetch when their tab first opens. Their weekly
 * intervals already express breaks, so no separate break editor exists.
 */
export function StaffDrawer({ member, onClose, onEdit, onChanged, canViewAttendance = false }: StaffDrawerProps) {
  const profile = useAsync(() => getStaff(member.id), [member.id])
  const [actionFailure, setActionFailure] = useState<string>()
  const [activeTab, setActiveTab] = useState<StaffTabId>('overview')
  const tabs = canViewAttendance ? TABS : TABS.filter(tab => tab.id !== 'attendance')

  const isActive = profile.data?.isActive ?? member.isActive

  function toggleStatus() {
    setActionFailure(undefined)

    setStaffStatus(member.id, !isActive)
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
    document.getElementById(`staff-tab-${next.id}`)?.focus()
  }

  function renderProfileTab(render: (profile: StaffProfile) => ReactNode) {
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
          <section className="tab-section" aria-label="Staff information">
            <div className="tab-section-head">
              <h4>Staff</h4>
            </div>
            <dl className="kv-grid">
              <dt>Email</dt>
              <dd>{profileData.email ?? '—'}</dd>
              <dt>Phone</dt>
              <dd>{profileData.phoneNumber ?? '—'}</dd>
              <dt>Calendar colour</dt>
              <dd>
                {profileData.calendarColor === null ? (
                  '—'
                ) : (
                  <>
                    <span
                      className="color-swatch"
                      style={{ backgroundColor: profileData.calendarColor }}
                      aria-hidden="true"
                    />
                    {profileData.calendarColor}
                  </>
                )}
              </dd>
              <dt>Member since</dt>
              <dd>{formatDate(profileData.createdAt)}</dd>
            </dl>
          </section>
        ))
      case 'services':
        return <ServiceAssignment staffId={member.id} />
      case 'hours':
        return <WorkingHoursEditor staffId={member.id} />
      case 'timeoff':
        return <TimeOffSection staffId={member.id} />
      case 'attendance':
        return <AttendanceSection staffId={member.id} />
      case 'details':
        return renderProfileTab(profileData => (
          <section className="tab-section" aria-label="Staff details">
            <div className="tab-section-head">
              <h4>Staff details</h4>
              <button type="button" className="button" onClick={() => onEdit(member.id)}>
                Edit
              </button>
            </div>
            <dl className="kv-grid">
              <dt>First name</dt>
              <dd>{profileData.firstName}</dd>
              <dt>Last name</dt>
              <dd>{profileData.lastName}</dd>
              <dt>Role</dt>
              <dd>{profileData.jobTitle ?? '—'}</dd>
              <dt>Email</dt>
              <dd>{profileData.email ?? '—'}</dd>
              <dt>Phone</dt>
              <dd>{profileData.phoneNumber ?? '—'}</dd>
              <dt>Status</dt>
              <dd><StatusPill isActive={profileData.isActive} /></dd>
              <dt>Position</dt>
              <dd>{profileData.sortOrder}</dd>
              <dt>Calendar colour</dt>
              <dd>
                {profileData.calendarColor === null ? (
                  '—'
                ) : (
                  <>
                    <span
                      className="color-swatch"
                      style={{ backgroundColor: profileData.calendarColor }}
                      aria-hidden="true"
                    />
                    {profileData.calendarColor}
                  </>
                )}
              </dd>
              <dt>Member since</dt>
              <dd>{formatDate(profileData.createdAt)}</dd>
            </dl>
          </section>
        ))
    }
  }

  return (
    <DetailDrawer
      label={`Staff ${member.firstName} ${member.lastName}`}
      onClose={onClose}
      panelClassName="drawer-workspace"
    >
      <DrawerHeader
        title={`${member.firstName} ${member.lastName}`}
        subtitle={
          <>
            <span>{member.jobTitle ?? '—'}</span>
            <StatusPill isActive={isActive} />
          </>
        }
        avatar={
          <Avatar
            firstName={member.firstName}
            lastName={member.lastName}
            color={member.calendarColor}
          />
        }
        actions={
          <button type="button" className="button" onClick={() => onEdit(member.id)}>
            Edit
          </button>
        }
        menu={
          <RowActionMenu
            label="More staff actions"
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
        aria-label="Staff sections"
        onKeyDown={handleTabKeys}
      >
        {tabs.map(tab => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            id={`staff-tab-${tab.id}`}
            aria-selected={activeTab === tab.id}
            aria-controls="staff-tabpanel"
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
        id="staff-tabpanel"
        aria-labelledby={`staff-tab-${activeTab}`}
      >
        {renderTab()}
      </div>
    </DetailDrawer>
  )
}
