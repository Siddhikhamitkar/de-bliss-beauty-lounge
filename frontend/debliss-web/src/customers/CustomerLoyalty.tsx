import { useState } from 'react'
import type { LoyaltyTransaction } from '../api/contracts.ts'
import { getCustomerLoyalty, getLoyaltyTransactions } from '../api/loyalty.ts'
import { EmptyState } from '../components/EmptyState.tsx'
import { ErrorState } from '../components/ErrorState.tsx'
import { LoadingState } from '../components/LoadingState.tsx'
import { formatDateTime } from '../format.ts'
import { useAsync } from '../useAsync.ts'

function Transactions({ items }: { items: LoyaltyTransaction[] }) {
  if (items.length === 0) return <EmptyState>No loyalty transactions yet.</EmptyState>
  return <table className="grid"><thead><tr><th scope="col">Date</th><th scope="col">Type</th><th scope="col" className="numeric">Points</th><th scope="col">Details</th></tr></thead>
    <tbody>{items.map(item => <tr key={item.id}><td>{formatDateTime(item.occurredAt)}</td><td>{item.type}</td><td className="numeric">{item.points.toFixed(2)}</td><td>{item.description}<p className="row-subtext">Invoice: {item.invoiceId}</p></td></tr>)}</tbody>
  </table>
}

function History({ customerId }: { customerId: string }) {
  const [page, setPage] = useState(1)
  const history = useAsync(() => getLoyaltyTransactions(customerId, page, 50).then(result => ({ result, page })), [customerId, page])
  const current = history.data?.page === page ? history.data.result : undefined
  return <>
    {history.failure !== undefined ? <ErrorState message={history.failure} onRetry={history.reload} /> : current === undefined ? <LoadingState label="Loading loyalty history…" /> : <>
      <Transactions items={current.items} />
      <div className="settings-actions" aria-label="Loyalty history pages">
        <button type="button" className="button" disabled={!current.hasPreviousPage} onClick={() => setPage(value => value - 1)}>Previous</button>
        <span>Page {current.page} of {Math.max(1, current.totalPages)} · {current.totalCount} transactions</span>
        <button type="button" className="button" disabled={!current.hasNextPage} onClick={() => setPage(value => value + 1)}>Next</button>
      </div>
    </>}
  </>
}

export function CustomerLoyalty({ customerId }: { customerId: string }) {
  const summary = useAsync(() => getCustomerLoyalty(customerId), [customerId])
  const [fullHistory, setFullHistory] = useState(false)
  return <section className="tab-section" aria-label="Customer loyalty">
    <div className="tab-section-head"><h4>Loyalty</h4></div>
    {summary.failure !== undefined ? <ErrorState message={summary.failure} onRetry={summary.reload} /> : summary.data === undefined ? <LoadingState label="Loading loyalty summary…" /> : <>
      <dl className="kv-grid">
        <dt>Available points</dt><dd>{summary.data.availablePoints.toFixed(2)}</dd>
        <dt>Lifetime points earned</dt><dd>{summary.data.lifetimePointsEarned.toFixed(2)}</dd>
        <dt>Points redeemed</dt><dd>{summary.data.pointsRedeemed.toFixed(2)}</dd>
        <dt>Points debt</dt><dd>{summary.data.pointsDebt.toFixed(2)}</dd>
      </dl>
      <p className="row-subtext">Read-only history. Points are earned and redeemed through checkout; reversals are recorded here.</p>
      <div className="tab-section-head"><h4>{fullHistory ? 'Full loyalty history' : 'Recent transactions'}</h4><button type="button" className="button" onClick={() => setFullHistory(value => !value)}>{fullHistory ? 'Show recent transactions' : 'View full history'}</button></div>
      {fullHistory ? <History key={customerId} customerId={customerId} /> : <Transactions items={summary.data.recentTransactions} />}
    </>}
  </section>
}
