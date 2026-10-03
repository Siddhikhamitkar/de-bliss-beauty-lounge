interface AvatarProps {
  firstName: string
  lastName: string
  /** The member's calendar colour; a neutral disc shows when none is set. */
  color: string | null
}

/** Initials in a coloured disc, so a member is recognisable in any list. */
export function Avatar({ firstName, lastName, color }: AvatarProps) {
  const initials = `${firstName.charAt(0)}${lastName.charAt(0)}`.toUpperCase()

  return (
    <span
      className="avatar"
      style={color === null ? undefined : { backgroundColor: color }}
      aria-hidden="true"
    >
      {initials}
    </span>
  )
}
