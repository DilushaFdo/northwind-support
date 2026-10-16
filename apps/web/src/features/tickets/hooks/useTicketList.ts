import { useEffect, useState } from 'react';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { PagedResult, TicketFilters, TicketListItem } from '../../../types/api';

const DEBOUNCE_MS = 300;

export const emptyFilters: TicketFilters = {
  search: '',
  status: undefined,
  priority: undefined,
  categoryId: undefined,
  customerId: undefined,
  assignedAgentId: undefined,
  unassignedOnly: false,
  page: 1,
  pageSize: 20,
  sortBy: 'createdAtUtc',
  sortDirection: 'desc',
};

interface State {
  data?: PagedResult<TicketListItem>;
  error?: string;
  /** The request that produced `data` or `error`. */
  settledKey?: string;
}

function useDebouncedValue<T>(value: T, delay: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const handle = window.setTimeout(() => setDebounced(value), delay);
    return () => window.clearTimeout(handle);
  }, [value, delay]);
  return debounced;
}

/** Owns the ticket list's filters and the data they produce. */
export function useTicketList() {
  const [filters, setFilters] = useState<TicketFilters>(emptyFilters);
  const [state, setState] = useState<State>({});
  const [reloadToken, setReloadToken] = useState(0);

  const debouncedSearch = useDebouncedValue(filters.search.trim(), DEBOUNCE_MS);
  const queryKey = JSON.stringify({ ...filters, search: debouncedSearch });
  const requestKey = `${queryKey}#${reloadToken}`;

  useEffect(() => {
    let ignore = false;
    const query = JSON.parse(queryKey) as TicketFilters;

    ticketsApi
      .getTickets(query)
      .then((data) => {
        if (!ignore) setState({ data, error: undefined, settledKey: requestKey });
      })
      .catch((error) => {
        if (ignore) return;
        const message = error instanceof ApiError ? error.message : 'Unable to load tickets.';
        setState((current) => ({ ...current, error: message, settledKey: requestKey }));
      });

    return () => {
      ignore = true;
    };
  }, [queryKey, requestKey]);

  const isLoading = state.settledKey !== requestKey;

  const updateFilters = (patch: Partial<TicketFilters>) =>
    setFilters((current) => ({ ...current, ...patch, page: patch.page ?? 1 }));

  const resetFilters = () => setFilters(emptyFilters);
  const reload = () => setReloadToken((token) => token + 1);

  return {
    filters,
    updateFilters,
    resetFilters,
    reload,
    isLoading,
    data: state.data,
    error: isLoading ? undefined : state.error,
  };
}
