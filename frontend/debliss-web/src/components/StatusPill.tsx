/** The one place record state gets a colour, so both pages read the same way. */
export function StatusPill({ isActive }: { isActive: boolean }) {
  return (
    <span className={isActive ? 'pill pill-active' : 'pill pill-inactive'}>
      {isActive ? 'Active' : 'Inactive'}
    </span>
  )
}
