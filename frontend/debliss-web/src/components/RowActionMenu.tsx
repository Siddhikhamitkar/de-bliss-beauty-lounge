import { useEffect, useRef, useState } from 'react'

export interface RowAction {
  label: string
  onSelect: () => void
  /** Destructive actions read in the danger colour. */
  isDanger?: boolean
}

interface RowActionMenuProps {
  /** Distinguishes the trigger per record, e.g. "Actions for Nadia Al Zaabi". */
  label: string
  actions: RowAction[]
  /** Trigger caption. Defaults to the ⋯ glyph the row menus use. */
  triggerText?: string
  /** Replaces the trigger's styling wholesale, for panel-level menus like "Add". */
  triggerClassName?: string
}

/**
 * A click-outside/Escape-aware dropdown. It swallows clicks and Escape while
 * open so the row (or drawer) underneath stays untouched.
 */
export function RowActionMenu({
  label,
  actions,
  triggerText,
  triggerClassName,
}: RowActionMenuProps) {
  const [isOpen, setIsOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!isOpen) {
      return
    }

    function handlePointerDown(event: MouseEvent) {
      if (rootRef.current !== null && !rootRef.current.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key !== 'Escape') {
        return
      }
      // Capture phase: the drawer behind the menu must not also close.
      event.stopPropagation()
      setIsOpen(false)
    }

    document.addEventListener('mousedown', handlePointerDown)
    document.addEventListener('keydown', handleKeyDown, true)
    return () => {
      document.removeEventListener('mousedown', handlePointerDown)
      document.removeEventListener('keydown', handleKeyDown, true)
    }
  }, [isOpen])

  return (
    <div className="row-menu" ref={rootRef} onClick={event => event.stopPropagation()}>
      <button
        type="button"
        className={triggerClassName ?? 'row-menu-trigger'}
        aria-haspopup="menu"
        aria-expanded={isOpen}
        aria-label={label}
        onClick={() => setIsOpen(open => !open)}
      >
        {triggerText ?? '⋯'}
      </button>
      {isOpen && (
        <ul className="row-menu-list" role="menu">
          {actions.map(action => (
            <li key={action.label} role="none">
              <button
                type="button"
                role="menuitem"
                className={action.isDanger === true ? 'row-menu-item is-danger' : 'row-menu-item'}
                onClick={() => {
                  setIsOpen(false)
                  action.onSelect()
                }}
              >
                {action.label}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
