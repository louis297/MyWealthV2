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
    createTradeSchedule: builder.mutation<{ id: string }, CreateTradeScheduleBody>({
      query: (body) => ({ url: "/fees/trade-schedules", method: "POST", body }),
      invalidatesTags: (_result, error) => (error ? [] : ["FeeSchedule"]),
    }),
  }),
});

export const { useGetAllTradeSchedulesQuery, useCreateTradeScheduleMutation } = feesApi;
