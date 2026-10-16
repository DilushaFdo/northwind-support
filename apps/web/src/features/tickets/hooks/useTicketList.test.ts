import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../../api/tickets';
import { page, ticketListItem } from '../../../test/fixtures';
import { useTicketList } from './useTicketList';
import type { TicketFilters } from '../../../types/api';

vi.mock('../../../api/tickets', () => ({
  ticketsApi: {
    getTickets: vi.fn(),
  },
}));

const getTickets = vi.mocked(ticketsApi.getTickets);

describe('useTicketList', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    getTickets.mockResolvedValue(page([ticketListItem()]));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('searches with the text the user typed, once the typing settles', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    act(() => {
      result.current.updateFilters({ search: 'invoice' });
    });

    // Still inside the debounce window: no second request yet.
    expect(getTickets).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: 'invoice', page: 1 }),
    );

    expect(result.current.isLoading).toBe(false);
    expect(result.current.data?.items).toHaveLength(1);
  });

    it.each<{ name: string; patch: Partial<TicketFilters> }>([
    { name: 'status', patch: { status: 'InProgress' } },
    { name: 'priority', patch: { priority: 'High' } },
    { name: 'category', patch: { categoryId: 2 } },
    { name: 'customer', patch: { customerId: 3 } },
    { name: 'assigned agent', patch: { assignedAgentId: 4 } },
    { name: 'unassigned only', patch: { unassignedOnly: true } },
  ])('requests immediately when $name changes (DEFECT-117)', async ({ patch }) => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    act(() => {
      result.current.updateFilters(patch);
    });

    // No time advanced past the debounce: a non-search filter must not wait for it.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining({ ...patch, page: 1 }));
  });

  it('keeps the chosen filter when search is typed afterwards', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    act(() => {
      result.current.updateFilters({ status: 'InProgress' });
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    act(() => {
      result.current.updateFilters({ search: 'printer' });
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ status: 'InProgress', search: 'printer' }),
    );
  });

  it('sends one request for a burst of keystrokes, not one per key', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    for (const text of ['a', 'ab', 'abc']) {
      act(() => {
        result.current.updateFilters({ search: text });
      });
      await act(async () => {
        await vi.advanceTimersByTimeAsync(100);
      });
    }

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    // The initial load plus exactly one request for the final text.
    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining({ search: 'abc' }));
  });
});
