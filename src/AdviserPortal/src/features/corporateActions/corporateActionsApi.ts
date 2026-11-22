import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

export type CorporateActionKind = "Split" | "Consolidation" | "Bonus";

export type PersonRef = { id: string; name: string };

/** corporate-actions R35: one corporate action of the firm. */
export type CorporateAction = {
  id: string;
  instrument: { id: string; symbol: string; name: string };
  kind: CorporateActionKind;
  ratioNew: number;
  ratioHeld: number;
  /** `yyyy-MM-dd`, the market's date. */
  exDate: string;
  recordDate: string | null;
  paymentDate: string | null;
  origin: "Feed" | "Manual";
  status: "Announced" | "Effective" | "Applied" | "Cancelled";
  memo: string | null;
  createdBy: PersonRef | null;
  createdAt: string;
  effectiveAt: string | null;
  effectiveBy: PersonRef | null;
  appliedAt: string | null;
  cancelledAt: string | null;
  cancelledBy: PersonRef | null;
  cancelNote: string | null;
  entitlements: { total: number; posted: number; pending: number; notApplied: number };
  rowVersion: string;
};

/** corporate-actions R36: what one account got. */
export type CorporateActionEntitlement = {
  accountId: string;
  accountName: string;
  customerId: string;
  customerName: string;
  status: "Pending" | "Posted" | "NotApplied";
  reason: "NoHolding" | "TooSmall" | null;
  quantityBefore: number | null;
  quantityAfter: number | null;
  transactionId: string | null;
  postedAt: string | null;
};

export type CorporateActionBody = {
  kind: string;
  ratioNew: number;
  ratioHeld: number;
  exDate: string;
  recordDate: string | null;
  paymentDate: string | null;
  memo: string | null;
};

/** The catalog as the form reads it: disabled instruments included, with the connection that serves each. */
export type CatalogInstrument = {
  id: string;
  symbol: string;
  name: string;
  isActive: boolean;
  marketDataConnection?: string | null;
};

export const corporateActionsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getCorporateActions: builder.query<PagedList<CorporateAction>, { page?: number; status?: string }>({
      query: ({ page = 1, status }) => `/corporate-actions?page=${page}${status ? `&status=${status}` : ""}`,
      providesTags: ["CorporateAction"],
    }),
    getCorporateAction: builder.query<CorporateAction, string>({
      query: (id) => `/corporate-actions/${id}`,
      providesTags: (_result, _error, id) => [{ type: "CorporateAction", id }],
    }),
    getCorporateActionEntitlements: builder.query<PagedList<CorporateActionEntitlement>, string>({
      query: (id) => `/corporate-actions/${id}/entitlements?pageSize=100`,
      providesTags: (_result, _error, id) => [{ type: "CorporateAction", id }],
    }),
    getCatalogInstruments: builder.query<PagedList<CatalogInstrument>, void>({
      query: () => "/instruments?page=1&pageSize=100",
    }),
    createCorporateAction: builder.mutation<{ id: string }, CorporateActionBody & { instrumentId: string }>({
      query: (body) => ({ url: "/corporate-actions", method: "POST", body }),
      invalidatesTags: (_result, error) => (error ? [] : ["CorporateAction"]),
    }),
    updateCorporateAction: builder.mutation<void, { id: string; body: CorporateActionBody & { rowVersion: string } }>({
      query: ({ id, body }) => ({ url: `/corporate-actions/${id}`, method: "PUT", body }),
      invalidatesTags: (_result, error) => (error ? [] : ["CorporateAction"]),
    }),
    cancelCorporateAction: builder.mutation<void, { id: string; rowVersion: string; note: string }>({
      query: ({ id, rowVersion, note }) => ({ url: `/corporate-actions/${id}/cancel`, method: "POST", body: { rowVersion, note } }),
      invalidatesTags: (_result, error) => (error ? [] : ["CorporateAction"]),
    }),
    applyCorporateAction: builder.mutation<void, { id: string; rowVersion: string }>({
      query: ({ id, rowVersion }) => ({ url: `/corporate-actions/${id}/apply`, method: "POST", body: { rowVersion } }),
      invalidatesTags: (_result, error) => (error ? [] : ["CorporateAction"]),
    }),
  }),
});

export const {
  useGetCorporateActionsQuery,
  useGetCorporateActionQuery,
  useGetCorporateActionEntitlementsQuery,
  useGetCatalogInstrumentsQuery,
  useCreateCorporateActionMutation,
  useUpdateCorporateActionMutation,
  useCancelCorporateActionMutation,
  useApplyCorporateActionMutation,
} = corporateActionsApi;
