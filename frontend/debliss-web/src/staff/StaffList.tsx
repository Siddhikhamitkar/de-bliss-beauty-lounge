import type { StaffSummary } from '../api/contracts.ts'
import { Avatar } from '../components/Avatar.tsx'
import { RowActionMenu } from '../components/RowActionMenu.tsx'
import { StatusPill } from '../components/StatusPill.tsx'

interface StaffListProps {
  members: StaffSummary[]
  onOpen: (member: StaffSummary) => void
  onToggleStatus: (member: StaffSummary) => void
}

export function StaffList({ members, onOpen, onToggleStatus }: StaffListProps) {
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
          <tr
            key={member.id}
            className="row-clickable"
            onClick={() => onOpen(member)}
          >
            <th scope="row">
              <button
                type="button"
                className="row-link"
                onClick={event => {
                  event.stopPropagation()
                  onOpen(member)
                }}
              >
                <span className="staff-cell">
                  <Avatar
                    firstName={member.firstName}
                    lastName={member.lastName}
                    color={member.calendarColor}
                  />
                  {member.firstName} {member.lastName}
                </span>
              </button>
            </th>
            <td>{member.jobTitle ?? '—'}</td>
            <td className="numeric">{member.serviceCount}</td>
            <td><StatusPill isActive={member.isActive} /></td>
            <td className="actions">
              <RowActionMenu
                label={`Actions for ${member.firstName} ${member.lastName}`}
                actions={[
                  {
                    label: member.isActive ? 'Deactivate' : 'Activate',
                    onSelect: () => onToggleStatus(member),
                    isDanger: member.isActive,
                  },
                ]}
              />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
