import { useEffect } from 'react'
import type { ReactNode } from 'react'

interface DetailDrawerProps {
  /** Names the dialog for assistive technology, e.g. "Customer details". */
  label: string
  onClose: () => void
  /** Denser records (staff) get the wider panel so their tables stay usable. */
  isWide?: boolean
  /** Extra class on the panel, for record-specific layout adjustments. */
  panelClassName?: string
  children: ReactNode
}

/**
 * The shared LIST → RECORD pattern: a right-hand panel over the still-visible
 * list. Escape closes it, except while a field is being edited or another
 * dialog is stacked on top — both keep their own Escape semantics.
 */
export function DetailDrawer({
  label,
  onClose,
  isWide = false,
  panelClassName,
  children,
}: DetailDrawerProps) {
  const panelClass = isWide ? 'drawer drawer-wide' : 'drawer'

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key !== 'Escape') {
        return
      }

      const target = event.target
      const isEditing =
        target instanceof HTMLElement &&
        (target.isContentEditable || target.closest('input, textarea, select') !== null)

      if (isEditing || document.querySelector('.dialog-backdrop') !== null) {
        return
      }

      onClose()
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  return (
    <div className="drawer-backdrop" onClick={onClose}>
      <aside
        className={panelClassName === undefined ? panelClass : `${panelClass} ${panelClassName}`}
        role="dialog"
        aria-modal="true"
        aria-label={label}
        onClick={event => event.stopPropagation()}
      >
        {children}
      </aside>
    </div>
  )
}

interface DrawerHeaderProps {
  title: string
  subtitle?: ReactNode
  avatar?: ReactNode
  actions?: ReactNode
  menu?: ReactNode
  onClose: () => void
}

/** Title block plus the record's actions; stays pinned while content scrolls. */
export function DrawerHeader({
  title,
  subtitle,
  avatar,
  actions,
  menu,
  onClose,
}: DrawerHeaderProps) {
  return (
    <header className="drawer-header">
      <div className="drawer-head-top">
        {avatar}
        <div className="drawer-head-text">
          <h3>{title}</h3>
          {subtitle !== undefined && <div className="drawer-head-sub">{subtitle}</div>}
        </div>
        <button type="button" className="drawer-close" onClick={onClose} aria-label="Close">
          ×
        </button>
      </div>
      {(actions !== undefined || menu !== undefined) && (
        <DrawerActions>
          {actions}
          {menu}
        </DrawerActions>
      )}
    </header>
  )
}

/** Horizontal strip for a record's actions, in the header or a section. */
export function DrawerActions({ children }: { children: ReactNode }) {
  return <div className="drawer-actions">{children}</div>
}

interface DrawerSectionProps {
  title: string
  actions?: ReactNode
  children: ReactNode
}

/** One titled block inside a drawer, hairline-separated from its siblings. */
export function DrawerSection({ title, actions, children }: DrawerSectionProps) {
  return (
    <section className="drawer-section">
      <div className="drawer-section-head">
        <h4>{title}</h4>
        {actions !== undefined && <DrawerActions>{actions}</DrawerActions>}
      </div>
      {children}
    </section>
  )
}
