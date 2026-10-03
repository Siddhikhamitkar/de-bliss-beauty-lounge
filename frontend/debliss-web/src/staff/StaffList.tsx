import type { StaffSummary } from '../api/contracts.ts'
import { Avatar } from '../components/Avatar.tsx'
import { StatusPill } from '../components/StatusPill.tsx'

interface StaffListProps {
  members: StaffSummary[]
  onView: (member: StaffSummary) => void
  onEdit: (member: StaffSummary) => void
  onToggleStatus: (member: StaffSummary) => void
}

export function StaffList({ members, onView, onEdit, onToggleStatus }: StaffListProps) {
  return (
    <table className="grid">
      <thead>
        <tr>
          <th scope="col">Staff member</th>
          <th scope="col">Role</th>
          <th scope="col" className="numeric">Services</th>
          <th scope="col">Status</th>
          <th scope="col">Actions</th>
        </tr>
      </thead>
      <tbody>
        {members.map(member => (
          <tr key={member.id}>
            <th scope="row">
              <span className="staff-cell">
                <Avatar
                  firstName={member.firstName}
                  lastName={member.lastName}
                  color={member.calendarColor}
                />
                {member.firstName} {member.lastName}
              </span>
            </th>
            <td>{member.jobTitle ?? '—'}</td>
            <td className="numeric">{member.serviceCount}</td>
            <td><StatusPill isActive={member.isActive} /></td>
            <td className="actions">
              <button type="button" className="button" onClick={() => onView(member)}>
                View
              </button>
              <button type="button" className="button" onClick={() => onEdit(member)}>
                Edit
              </button>
              <button
                type="button"
                className="button"
                onClick={() => onToggleStatus(member)}
              >
                {member.isActive ? 'Deactivate' : 'Activate'}
              </button>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
