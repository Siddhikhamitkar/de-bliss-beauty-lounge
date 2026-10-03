import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { useAsync } from './useAsync.ts'

function Probe({ id, load }: { id: string; load: () => Promise<string> }) {
  const state = useAsync(() => load().then(text => `${text} ${id}`), [id])
  return (
    <div>
      <output>{state.data}</output>
      <output>{state.failure}</output>
      <button type="button" onClick={state.reload}>
        Reload
      </button>
    </div>
  )
}

describe('useAsync', () => {
  it('renders the loaded data', async () => {
    render(<Probe id="a" load={() => Promise.resolve('value')} />)
    expect(await screen.findByText('value a')).toBeInTheDocument()
  })

  it('renders the failure sentence when the loader rejects', async () => {
    render(<Probe id="a" load={() => Promise.reject(new Error('boom'))} />)
    expect(await screen.findByText('boom')).toBeInTheDocument()
  })

  it('explains a TypeError as the API being unreachable', async () => {
    render(<Probe id="a" load={() => Promise.reject(new TypeError('fetch failed'))} />)
    expect(
      await screen.findByText('Cannot reach the De Bliss API. Is the backend running?'),
    ).toBeInTheDocument()
  })

  it('reloads when a dependency changes', async () => {
    const load = vi.fn().mockResolvedValue('value')
    const { rerender } = render(<Probe id="a" load={load} />)
    await screen.findByText('value a')

    rerender(<Probe id="b" load={load} />)
    expect(await screen.findByText('value b')).toBeInTheDocument()
    expect(load).toHaveBeenCalledTimes(2)
  })

  it('reloads when the caller asks', async () => {
    const user = userEvent.setup()
    const load = vi.fn().mockResolvedValue('value')
    render(<Probe id="a" load={load} />)
    await screen.findByText('value a')

    await user.click(screen.getByRole('button', { name: 'Reload' }))
    await screen.findByText('value a')
    expect(load).toHaveBeenCalledTimes(2)
  })
})
