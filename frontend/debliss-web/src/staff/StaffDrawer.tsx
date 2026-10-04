import { useState } from 'react'
import { describeFailure } from '../api/client.ts'
import { getStaff, setStaffStatus } from '../api/staff.ts'
import type { StaffSummary } from '../api/contracts.ts'
import { Avatar } from '../components/Avatar.tsx'
import { DetailDrawer, DrawerHeader, DrawerSection } from '../components/DetailDrawer.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { RowActionMenu } from '../components/RowActionMenu.tsx'
import { StatusPill } from '../components/StatusPill.tsx'
import { useAsync } from '../useAsync.ts'
import { ServiceAssignment } from './ServiceAssignment.tsx'
import { TimeOffSection } from './TimeOffSection.tsx'
import { WorkingHoursEditor } from './WorkingHoursEditor.tsx'

interface StaffDrawerProps {
  member: StaffSummary
  onClose: () => void
  onEdit: (staffId: string) => void
  onChanged: () => void
}

/**
 * The member's profile as a wide right-hand drawer: contact details plus the
 * same service-assignment, working-hours and time-off panels the full page
 * carried, reused verbatim. Their weekly intervals already express breaks, so
 * no separate break editor exists or is needed.
 */
export function StaffDrawer({ member, onClose, onEdit, onChanged }: StaffDrawerProps) {
  const profile = useAsync(() => getStaff(member.id), [member.id])
  const [actionFailure, setActionFailure] = useState<string>()

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

  const profileData = profile.data

  return (
    <DetailDrawer
      label={`Staff ${member.firstName} ${member.lastName}`}
      isWide
      onClose={onClose}
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
        <div className="drawer-section">
          <p className="banner" role="alert">{actionFailure}</p>
        </div>
      )}

      {profile.failure !== undefined ? (
        <div className="drawer-section">
          <ErrorState message={profile.failure} onRetry={profile.reload} />
        </div>
      ) : profileData === undefined ? (
        <div className="drawer-section">
          <LoadingState label="Loading profile…" />
        </div>
      ) : (
        <>
          <DrawerSection title="Overview">
            <dl className="detail-grid">
              <dt>Role</dt>
              <dd>{profileData.jobTitle ?? '—'}</dd>
              <dt>Status</dt>
              <dd><StatusPill isActive={profileData.isActive} /></dd>
              <dt>Email</dt>
              <dd>{profileData.email ?? '—'}</dd>
              <dt>Phone</dt>
              <dd>{profileData.phoneNumber ?? '—'}</dd>
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
            </dl>
          </DrawerSection>

          <ServiceAssignment staffId={member.id} />
          <WorkingHoursEditor staffId={member.id} />
          <TimeOffSection staffId={member.id} />
        </>
      )}
    </DetailDrawer>
  )
}
