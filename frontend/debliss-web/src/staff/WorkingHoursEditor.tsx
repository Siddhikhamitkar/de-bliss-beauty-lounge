import { useState } from 'react'
import { ApiError, describeFailure } from '../api/client.ts'
import { getWeekSchedule, saveWeekSchedule } from '../api/staff.ts'
import type { DaySchedule, WorkingInterval } from '../api/contracts.ts'
import { useAsync } from '../useAsync.ts'
import { FieldErrors } from '../components/FieldErrors.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'

/** The wire sends day names; this is the order the week is always shown in. */
const Weekdays = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
] as const

function emptyWeek(): DaySchedule[] {
  return Weekdays.map(day => ({ dayOfWeek: day, isWorkingDay: false, intervals: [] }))
}

// The API always returns the full week, but the editor repairs the order (and
// fills any missing day as off) so the draft is safe to render either way.
function normalizeDays(days: DaySchedule[]): DaySchedule[] {
  return Weekdays.map(
    day =>
      days.find(entry => entry.dayOfWeek === day) ?? {
        dayOfWeek: day,
        isWorkingDay: false,
        intervals: [],
      },
  )
}

/**
 * The recurring weekly pattern. View mode reads as a week rota; edit mode
 * offers one row per day with any number of HH:mm intervals. The gaps between
 * a day's intervals are the member's breaks, so no separate break editor is
 * needed — that is how availability already interprets them.
 */
export function WorkingHoursEditor({ staffId }: { staffId: string }) {
  const schedule = useAsync(() => getWeekSchedule(staffId), [staffId])
  const [isEditing, setIsEditing] = useState(false)
  const [draft, setDraft] = useState<DaySchedule[]>([])
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [failure, setFailure] = useState<string>()
  const [isSaving, setIsSaving] = useState(false)

  function startEditing() {
    setDraft(schedule.data === undefined ? emptyWeek() : normalizeDays(schedule.data.days))
    setFieldErrors({})
    setFailure(undefined)
    setIsEditing(true)
  }

  function setDay(dayOfWeek: string, patch: Partial<DaySchedule>) {
    setDraft(current =>
      current.map(day => (day.dayOfWeek === dayOfWeek ? { ...day, ...patch } : day)),
    )
  }

  function setInterval(dayOfWeek: string, index: number, patch: Partial<WorkingInterval>) {
    setDraft(current =>
      current.map(day =>
        day.dayOfWeek === dayOfWeek
          ? {
              ...day,
              intervals: day.intervals.map((interval, i) =>
                i === index ? { ...interval, ...patch } : interval,
              ),
            }
          : day,
      ),
    )
  }

  async function save() {
    setFailure(undefined)
    setFieldErrors({})
    setIsSaving(true)
    try {
      await saveWeekSchedule(staffId, draft)
      setIsEditing(false)
      schedule.reload()
    } catch (error) {
      if (error instanceof ApiError && Object.keys(error.errors).length > 0) {
        setFieldErrors(error.errors)
      } else {
        setFailure(describeFailure(error))
      }
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <section className="panel" aria-label="Working hours">
      <div className="panel-header">
        <h2>Working hours</h2>
        {!isEditing && (
          <button type="button" className="button" onClick={startEditing}>
            Manage working hours
          </button>
        )}
      </div>

      {failure !== undefined && <p className="banner" role="alert">{failure}</p>}

      {schedule.failure !== undefined && (
        <ErrorState message={schedule.failure} onRetry={schedule.reload} />
      )}

      {schedule.failure === undefined && schedule.data === undefined && (
        <LoadingState label="Loading working hours…" />
      )}

      {schedule.failure === undefined && schedule.data !== undefined && (
        isEditing ? (
          <form
            className="week-editor"
            onSubmit={event => {
              event.preventDefault()
              void save()
            }}
            noValidate
          >
            {draft.map(day => (
              <div key={day.dayOfWeek} className="day-row">
                <label className="checkbox day-name">
                  <input
                    type="checkbox"
                    checked={day.isWorkingDay}
                    onChange={event =>
                      setDay(day.dayOfWeek, {
                        isWorkingDay: event.target.checked,
                        // Turning a day off discards its intervals; turning it
                        // back on starts from one full-time block.
                        intervals: event.target.checked && day.intervals.length === 0
                          ? [{ start: '09:00', end: '17:00' }]
                          : day.intervals,
                      })
                    }
                  />
                  {day.dayOfWeek}
                </label>

                {day.isWorkingDay && (
                  <div className="interval-list">
                    {day.intervals.map((interval, index) => (
                      <div key={index} className="interval-row">
                        <input
                          aria-label={`${day.dayOfWeek} interval ${index + 1} start`}
                          value={interval.start}
                          placeholder="09:00"
                          onChange={event =>
                            setInterval(day.dayOfWeek, index, { start: event.target.value })
                          }
                        />
                        <span aria-hidden="true">–</span>
                        <input
                          aria-label={`${day.dayOfWeek} interval ${index + 1} end`}
                          value={interval.end}
                          placeholder="17:00"
                          onChange={event =>
                            setInterval(day.dayOfWeek, index, { end: event.target.value })
                          }
                        />
                        <button
                          type="button"
                          className="button"
                          onClick={() =>
                            setDay(day.dayOfWeek, {
                              intervals: day.intervals.filter((_, i) => i !== index),
                            })
                          }
                        >
                          Remove
                        </button>
                      </div>
                    ))}
                    <button
                      type="button"
                      className="button"
                      onClick={() =>
                        setDay(day.dayOfWeek, {
                          intervals: [...day.intervals, { start: '09:00', end: '17:00' }],
                        })
                      }
                    >
                      Add interval
                    </button>
                  </div>
                )}
              </div>
            ))}

            <FieldErrors messages={fieldErrors.days} />

            <div className="form-actions">
              <button type="submit" className="button button-primary" disabled={isSaving}>
                {isSaving ? 'Saving…' : 'Save working hours'}
              </button>
              <button
                type="button"
                className="button"
                onClick={() => setIsEditing(false)}
                disabled={isSaving}
              >
                Cancel
              </button>
            </div>
          </form>
        ) : (
          <table className="grid">
            <tbody>
              {normalizeDays(schedule.data.days).map(day => (
                <tr key={day.dayOfWeek}>
                  <th scope="row">{day.dayOfWeek}</th>
                  <td>
                    {day.isWorkingDay
                      ? day.intervals.map(interval => `${interval.start} – ${interval.end}`).join(', ')
                      : 'Off'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      )}
    </section>
  )
}
