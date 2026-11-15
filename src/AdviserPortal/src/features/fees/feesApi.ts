import type { FetchBaseQueryError } from "@reduxjs/toolkit/query";
import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

/** trade-fees §8.1. `from` is in the quote currency; `ratePercent` 0.5 means 0.5 %. */
export type FeeBand = {
  from: number;
  ratePercent: number;
};

export type TradeFeeSchedule = {
  id: string;
  quoteCurrency: string;
  /** UTC date `yyyy-MM-dd`. Never passed to `new Date`. */
  effectiveFrom: string;
  minimum: number;
  cap: number | null;
  bands: FeeBand[];
  inEffect: boolean;
  rowVersion: string;
};

export type CreateTradeScheduleBody = {
  quoteCurrency: string;
  effectiveFrom: string;
  minimum: number;
  cap: number | null;
  bands: FeeBand[];
};

/** R31: the schedule fields of a create body, without `effectiveFrom`, plus 1 to 5 amounts. */
export type TradeFeePreviewBody = {
  quoteCurrency: string;
  minimum: number;
  cap: number | null;
  bands: FeeBand[];
  amounts: number[];
};

/** R32. `slice`, band `fee` and `banded` are exact; only `fee` is rounded. */
export type TradeFeePreviewItem = {
  amount: number;
  bands: { from: number; ratePercent: number; slice: number; fee: number }[];
  banded: number;
  limitApplied: "minimum" | "cap" | null;
  fee: number;
};

export type TradeFeePreview = {
  currency: string;
  items: TradeFeePreviewItem[];
};

/** R11: the create fields without `quoteCurrency`, plus `rowVersion`. */
export type UpdateTradeScheduleBody = Omit<CreateTradeScheduleBody, "quoteCurrency"> & { rowVersion: string };

export const feesApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getAllTradeSchedules: builder.query<TradeFeeSchedule[], void>({
      // adviser-portal-fees: page through the envelope 100 rows at a time until every row is loaded.
      async queryFn(_arg, _api, _extra, baseQuery) {
        const items: TradeFeeSchedule[] = [];
        for (let page = 1; ; page += 1) {
          const result = await baseQuery(`/fees/trade-schedules?pageSize=100&page=${page}`);
          if (result.error) {
            return { error: result.error as FetchBaseQueryError };
          }
          const data = result.data as PagedList<TradeFeeSchedule>;
          items.push(...data.items);
          if (items.length >= data.totalCount || data.items.length === 0) {
            return { data: items };
          }
        }
      },
      providesTags: ["FeeSchedule"],
    }),
    getTradeSchedule: builder.query<TradeFeeSchedule, string>({
      query: (id) => `/fees/trade-schedules/${id}`,
      providesTags: (_result, _error, id) => [{ type: "FeeSchedule", id }],
    }),
    deleteTradeSchedule: builder.mutation<void, string>({
      query: (id) => ({ url: `/fees/trade-schedules/${id}`, method: "DELETE" }),
      // A refused delete changed nothing; the page reloads the row itself on fee_schedule_in_effect (F9).
      invalidatesTags: (_result, error) => (error ? [] : ["FeeSchedule"]),
    }),
    updateTradeSchedule: builder.mutation<void, { id: string; body: UpdateTradeScheduleBody }>({
      query: ({ id, body }) => ({ url: `/fees/trade-schedules/${id}`, method: "PUT", body }),
      // A refused update changed nothing; both 409 codes reload the row explicitly (F8, F9).
      invalidatesTags: (_result, error) => (error ? [] : ["FeeSchedule"]),
    }),
    createTradeSchedule: builder.mutation<{ id: string }, CreateTradeScheduleBody>({
      query: (body) => ({ url: "/fees/trade-schedules", method: "POST", body }),
      invalidatesTags: (_result, error) => (error ? [] : ["FeeSchedule"]),
    }),
    // A POST that computes and writes nothing (R33), so it is a query; the base query adds the BFF header.
    previewTradeFees: builder.query<TradeFeePreview, TradeFeePreviewBody>({
      query: (body) => ({ url: "/fees/trade-schedules/preview", method: "POST", body }),
      keepUnusedDataFor: 0,
    }),
  }),
});

export const {
  useGetAllTradeSchedulesQuery,
  useCreateTradeScheduleMutation,
  useDeleteTradeScheduleMutation,
  useGetTradeScheduleQuery,
  usePreviewTradeFeesQuery,
  useUpdateTradeScheduleMutation,
} = feesApi;
