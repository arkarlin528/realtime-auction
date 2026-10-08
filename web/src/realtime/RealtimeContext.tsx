import {
  HttpTransportType, type HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel,
} from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import type {
  AuctionClosedMessage, AuctionDetail, AuctionSummary, BidPlacedMessage, OutbidMessage,
} from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { syncServerClock } from '../lib/clock'

export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'offline'

export interface Toast {
  id: number
  tone: 'warning' | 'success' | 'info'
  title: string
  body: string
  auctionId?: number
}

interface RealtimeValue {
  state: LiveState
  toasts: Toast[]
  dismiss: (id: number) => void
  join: (group: string) => void
  leave: (group: string) => void
}

const RealtimeContext = createContext<RealtimeValue | null>(null)

/**
 * One SignalR connection for the whole app. Incoming messages patch the React Query cache directly,
 * so every component showing that auction re-renders with the new price; no refetch, no polling.
 *
 * WebSockets only, with negotiation skipped: then any API instance can take the connection, and a
 * load balancer doesn't need sticky sessions (see docs/adr/0003-scale-out-with-redis-backplane.md).
 */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { token, user } = useAuth()
  const queryClient = useQueryClient()
  const [state, setState] = useState<LiveState>('connecting')
  const [toasts, setToasts] = useState<Toast[]>([])
  const connectionRef = useRef<HubConnection | null>(null)
  const groups = useRef(new Map<string, number>()) // group -> number of components using it
  const userIdRef = useRef(user?.id)
  useEffect(() => {
    userIdRef.current = user?.id
  }, [user?.id])

  const pushToast = (toast: Omit<Toast, 'id'>) => {
    const id = Date.now() + Math.random()
    setToasts(list => [...list.slice(-3), { ...toast, id }])
    setTimeout(() => setToasts(list => list.filter(t => t.id !== id)), 7000)
  }

  useEffect(() => {
    // Rebuilt when the token changes, so "you've been outbid" messages follow the signed-in user.
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/auctions', {
        accessTokenFactory: token ? () => token : undefined,
        skipNegotiation: true,
        transport: HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect()
      .configureLogging(import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning)
      .build()
    connectionRef.current = connection

    const rejoin = async () => {
      for (const group of groups.current.keys()) await invokeJoin(connection, group)
    }

    connection.on('BidPlaced', (m: BidPlacedMessage) => {
      syncServerClock(m.serverTime)
      applyBid(queryClient, m)
    })
    connection.on('AuctionClosed', (m: AuctionClosedMessage) => {
      void queryClient.invalidateQueries({ queryKey: ['auction', m.auctionId] })
      void queryClient.invalidateQueries({ queryKey: ['auctions'] })
      void queryClient.invalidateQueries({ queryKey: ['my-bids'] })
      if (m.winnerId != null && m.winnerId === userIdRef.current) {
        pushToast({ tone: 'success', title: 'You won! 🎉', body: m.title, auctionId: m.auctionId })
      }
    })
    connection.on('AuctionCreated', () => void queryClient.invalidateQueries({ queryKey: ['auctions'] }))
    connection.on('Outbid', (m: OutbidMessage) => {
      void queryClient.invalidateQueries({ queryKey: ['my-bids'] })
      pushToast({
        tone: 'warning',
        title: "You've been outbid",
        body: `${m.title}: now $${m.newPrice.toLocaleString()}`,
        auctionId: m.auctionId,
      })
    })
    // A replaced connection (after login/logout) must not overwrite the state of its successor.
    const isCurrent = () => connectionRef.current === connection
    connection.onreconnecting(() => isCurrent() && setState('reconnecting'))
    connection.onreconnected(async () => {
      if (!isCurrent()) return
      setState('live')
      await rejoin()
      // We may have missed messages while disconnected: refresh what's on screen.
      void queryClient.invalidateQueries()
    })
    connection.onclose(() => isCurrent() && setState('offline'))

    const started = connection.start()
      .then(async () => {
        if (!isCurrent()) return
        setState('live')
        await rejoin()
      })
      .catch(() => isCurrent() && setState('offline'))

    return () => {
      connectionRef.current = null
      // Stopping mid-start makes SignalR log an error; let the start finish first.
      void started.finally(() => connection.stop())
    }
  }, [token, queryClient])

  // Reference-counted, so two components showing the same auction share one group membership.
  const join = useCallback((key: string) => {
    groups.current.set(key, (groups.current.get(key) ?? 0) + 1)
    const connection = connectionRef.current
    if (connection?.state === HubConnectionState.Connected) void invokeJoin(connection, key)
  }, [])

  const leave = useCallback((key: string) => {
    const count = (groups.current.get(key) ?? 1) - 1
    if (count > 0) {
      groups.current.set(key, count)
      return
    }
    groups.current.delete(key)
    const connection = connectionRef.current
    if (connection?.state === HubConnectionState.Connected) {
      ;(key === 'lobby' ? connection.invoke('LeaveLobby') : connection.invoke('LeaveAuction', Number(key)))
        .catch(() => undefined) // connection closing anyway; the group membership dies with it
    }
  }, [])

  const dismiss = useCallback((id: number) => setToasts(list => list.filter(t => t.id !== id)), [])
  const value = useMemo<RealtimeValue>(() => ({ state, toasts, dismiss, join, leave }), [state, toasts, dismiss, join, leave])

  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>
}

/** Joins a group. Never rejects: if the connection drops, onreconnected re-joins everything. */
function invokeJoin(connection: HubConnection, group: string) {
  const call = group === 'lobby' ? connection.invoke('JoinLobby') : connection.invoke('JoinAuction', Number(group))
  return call.catch(() => undefined)
}

/** Patch every cached view of this auction with the new bid. */
function applyBid(queryClient: ReturnType<typeof useQueryClient>, m: BidPlacedMessage) {
  const patch = (a: AuctionSummary): AuctionSummary => a.id !== m.auctionId ? a : {
    ...a,
    currentPrice: m.amount,
    minimumNextBid: m.minimumNextBid,
    bidCount: m.bidCount,
    endsAt: m.endsAt,
    leadingBidderId: m.bidderId,
    reserveMet: m.reserveMet,
  }

  queryClient.setQueriesData<AuctionSummary[]>({ queryKey: ['auctions'] }, list => list?.map(patch))
  queryClient.setQueryData<AuctionDetail>(['auction', m.auctionId], detail => {
    if (!detail || detail.recentBids.some(b => b.id === m.bidId)) return detail
    return {
      ...detail,
      summary: patch(detail.summary),
      recentBids: [
        { id: m.bidId, amount: m.amount, bidderId: m.bidderId, bidderName: m.bidderName, placedAt: m.placedAt },
        ...detail.recentBids,
      ].slice(0, 25),
    }
  })
}

export function useRealtime(): RealtimeValue {
  const value = useContext(RealtimeContext)
  if (!value) throw new Error('useRealtime must be used inside <RealtimeProvider>')
  return value
}

/** Receive live updates for an auction room (or the lobby) while the calling component is mounted. */
export function useGroup(group: number | 'lobby') {
  const { join, leave } = useRealtime()
  useEffect(() => {
    const key = String(group)
    join(key)
    return () => leave(key)
  }, [group, join, leave])
}
