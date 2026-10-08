import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../auth/AuthContext'
import { api } from './client'
import type { AuctionDetail, AuctionListFilter } from './types'

// Live data arrives over SignalR and is written into these caches, so queries rarely need refetching.

export function useAuctions(filter: AuctionListFilter) {
  return useQuery({ queryKey: ['auctions', filter], queryFn: () => api.auctions(filter) })
}

export function useAuction(id: number) {
  return useQuery({ queryKey: ['auction', id], queryFn: () => api.auction(id) })
}

export function useMyBids() {
  const { user } = useAuth()
  return useQuery({ queryKey: ['my-bids', user?.id], queryFn: api.myBids, enabled: !!user })
}

export function usePlaceBid(auctionId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ amount, key }: { amount: number; key: string }) => api.placeBid(auctionId, amount, key),
    // Safe to retry a dropped request because the idempotency key is the same on every attempt.
    retry: (count, error) => count < 2 && 'status' in error && error.status === 0,
    onSuccess: result => {
      queryClient.setQueryData<AuctionDetail>(['auction', auctionId], detail => detail && {
        ...detail,
        summary: {
          ...detail.summary,
          minimumNextBid: Math.max(detail.summary.minimumNextBid, result.minimumNextBid),
          endsAt: result.endsAt,
        },
      })
      void queryClient.invalidateQueries({ queryKey: ['my-bids'] })
    },
  })
}
