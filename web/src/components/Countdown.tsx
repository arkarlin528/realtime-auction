import { formatRemaining, useServerNow } from '../lib/clock'

/** Counts down (or up to the start) using server time. Turns urgent in the last minute. */
export function Countdown({ startsAt, endsAt, closed }: { startsAt: string; endsAt: string; closed: boolean }) {
  const now = useServerNow()
  const start = new Date(startsAt).getTime()
  const end = new Date(endsAt).getTime()

  if (closed) return <span className="countdown done">Ended</span>
  if (now < start) return <span className="countdown upcoming">Starts in {formatRemaining(start - now)}</span>
  if (now >= end) return <span className="countdown done">Closing…</span>

  const left = end - now
  const tone = left < 30_000 ? 'critical' : left < 60_000 ? 'urgent' : ''
  return <span className={`countdown ${tone}`}>{formatRemaining(left)} left</span>
}
