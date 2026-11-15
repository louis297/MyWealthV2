import { api } from "@/shared/api/api";

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

export const feesApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getAllTradeSchedules: builder.query<TradeFeeSchedule[], void>({
      queryFn: () => ({ data: [] }),
      providesTags: ["FeeSchedule"],
    }),
  }),
});

export const { useGetAllTradeSchedulesQuery } = feesApi;
