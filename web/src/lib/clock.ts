import { useSyncExternalStore } from 'react'

/**
 * The server decides when an auction ends, so countdowns should use the server's clock, not the
 * laptop's (which can be minutes off). We keep an offset = serverNow - localNow and apply it.
 */
let offsetMs = 0

export function syncServerClock(serverTimeIso: string, requestStartedAt = Date.now(), requestEndedAt = Date.now()) {
  // Assume the server stamped the time halfway through the round trip.
  const localMidpoint = (requestStartedAt + requestEndedAt) / 2
  offsetMs = new Date(serverTimeIso).getTime() - localMidpoint
}

export function serverNow(): number {
  return Date.now() + offsetMs
}

// One shared 250 ms ticker for every countdown on the page (instead of one interval per card).
const listeners = new Set<() => void>()
let tick = serverNow()
let timer: ReturnType<typeof setInterval> | null = null

function subscribe(listener: () => void) {
  listeners.add(listener)
  timer ??= setInterval(() => {
    tick = serverNow()
    listeners.forEach(l => l())
  }, 250)
  return () => {
    listeners.delete(listener)
    if (listeners.size === 0 && timer) {
      clearInterval(timer)
      timer = null
    }
  }
}

/** Current server time in ms, re-rendering 4 times a second. */
export function useServerNow(): number {
  return useSyncExternalStore(subscribe, () => tick)
}

export function formatRemaining(ms: number): string {
  if (ms <= 0) return '0s'
  const total = Math.ceil(ms / 1000)
  const days = Math.floor(total / 86400)
  const hours = Math.floor((total % 86400) / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = total % 60
  if (days > 0) return `${days}d ${hours}h`
  if (hours > 0) return `${hours}h ${minutes}m`
  if (minutes > 0) return `${minutes}m ${seconds.toString().padStart(2, '0')}s`
  return `${seconds}s`
}
