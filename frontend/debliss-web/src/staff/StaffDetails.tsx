import { useState } from 'react'
import { describeFailure } from '../api/client.ts'
import { getStaff, setStaffStatus } from '../api/staff.ts'
import { useAsync } from '../useAsync.ts'
import { Avatar } from '../components/Avatar.tsx'
import { StatusPill } from '../components/StatusPill.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { ServiceAssignment } from './ServiceAssignment.tsx'
import { WorkingHoursEditor } from './WorkingHoursEditor.tsx'
import { TimeOffSection } from './TimeOffSection.tsx'

interface StaffDetailsProps {
  staffId: string
  onBack: () => void
  onEdit: (staffId: string) => void
}

/**
 * One member's profile: contact details plus the three sections the future
 * appointment calendar will consume — service capabilities, the weekly
 * working pattern, and dated time off.
 */
export function StaffDetails({ staffId, onBack, onEdit }: StaffDetailsProps) {
  const staff = useAsync(() => getStaff(staffId), [staffId])
  const [actionFailure, setActionFailure] = useState<string>()

  function toggleStatus() {
    if (staff.data === undefined) {
      return
    }

    setActionFailure(undefined)
    setStaffStatus(staff.data.id, !staff.data.isActive)
      .then(() => staff.reload())
      .catch((error: unknown) => setActionFailure(describeFailure(error)))
  }

  if (staff.failure !== undefined) {
    return (
      <section className="panel" aria-label="Staff profile">
        <div className="panel-header">
          <h2>Staff profile</h2>
        </div>
        <ErrorState message={staff.failure} onRetry={staff.reload} />
        <div className="form-actions">
          <button type="button" className="button" onClick={onBack}>
            Back to staff
          </button>
        </div>
      </section>
    )
  }

  if (staff.data === undefined) {
    return (
      <section className="panel" aria-label="Staff profile">
        <div className="panel-header">
          <h2>Staff profile</h2>
        </div>
        <LoadingState label="Loading profile…" />
      </section>
    )
  }

  const member = staff.data

  return (
    <>
      <section className="panel" aria-label="Staff profile">
        <div className="panel-header">
          <h2>
            <span className="staff-cell">
              <Avatar
                firstName={member.firstName}
                lastName={member.lastName}
                color={member.calendarColor}
              />
              {member.firstName} {member.lastName}
            </span>
          </h2>
          <div className="actions">
            <button type="button" className="button" onClick={() => onEdit(member.id)}>
              Edit
            </button>
            <button type="button" className="button" onClick={toggleStatus}>
              {member.isActive ? 'Deactivate' : 'Activate'}
            </button>
            <button type="button" className="button" onClick={onBack}>
              Back to staff
            </button>
          </div>
        </div>

        {actionFailure !== undefined && (
          <p className="banner" role="alert">{actionFailure}</p>
        )}

        <dl className="detail-grid">
          <dt>Status</dt>
          <dd><StatusPill isActive={member.isActive} /></dd>
          <dt>Role</dt>
          <dd>{member.jobTitle ?? '—'}</dd>
          <dt>Email</dt>
          <dd>{member.email ?? '—'}</dd>
          <dt>Phone</dt>
          <dd>{member.phoneNumber ?? '—'}</dd>
          <dt>Position</dt>
          <dd>{member.sortOrder}</dd>
          <dt>Calendar colour</dt>
          <dd>
            {member.calendarColor === null ? (
              '—'
            ) : (
              <>
                <span
                  className="color-swatch"
                  style={{ backgroundColor: member.calendarColor }}
                  aria-hidden="true"
                />
                {member.calendarColor}
              </>
            )}
          </dd>
        </dl>
      </section>

      <ServiceAssignment staffId={member.id} />
      <WorkingHoursEditor staffId={member.id} />
      <TimeOffSection staffId={member.id} />
    </>
  )
}
