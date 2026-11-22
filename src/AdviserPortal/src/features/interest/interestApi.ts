import type { FetchBaseQueryError } from "@reduxjs/toolkit/query";
import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

/** interest-run R8: a Customer's tax profile with the withholding it gives. */
export type TaxProfile = {
  taxResidency: "NewZealand" | "Overseas";
  irdNumberHeld: boolean;
  rwtRatePercent: number | null;
  withholding: { kind: string; ratePercent: number; basis: string };
  updatedBy: { id: string; name: string } | null;
  updatedAt: string | null;
  /** Null while no profile is saved (R6). */
  rowVersion: string | null;
};

export type TaxProfileBody = {
  taxResidency: string;
  irdNumberHeld: boolean;
  rwtRatePercent: number | null;
  rowVersion: string | null;
};

/** interest-run R9: `ratePercent` is a rate per year; `from` is in the currency. */
export type RateBand = { from: number; ratePercent: number };

export type InterestRateSchedule = {
  id: string;
  currency: string;
  /** UTC date `yyyy-MM-dd`. */
  effectiveFrom: string;
  bands: RateBand[];
  inEffect: boolean;
  rowVersion: string;
};

export type CreateInterestRateScheduleBody = { currency: string; effectiveFrom: string; bands: RateBand[] };

export type UpdateInterestRateScheduleBody = { effectiveFrom: string; bands: RateBand[]; rowVersion: string };

/** interest-run R37: one run and its totals per currency. */
export type InterestRun = {
  id: string;
  asOfDate: string;
  postingDate: string;
  trigger: "Scheduled" | "Manual" | "AccountClosure";
  ranBy: { id: string; name: string } | null;
  accountId: string | null;
  startedAt: string;
  completedAt: string | null;
  accountsProcessed: number;
  accountsFailed: number;
  postingCount: number;
  totals: { currency: string; postingCount: number; grossAmount: number; taxAmount: number }[];
};

/** interest-run R36: one day's interest on an account. */
export type InterestAccrual = {
  date: string;
  balance: number;
  ratePercent: number;
  scheduleId: string;
  amount: number;
  transactionId: string | null;
};

export const interestApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getCustomerTaxProfile: builder.query<TaxProfile, string>({
      query: (customerId) => `/users/customers/${customerId}/tax-profile`,
      providesTags: (_result, _error, id) => [{ type: "TaxProfile", id }],
    }),
    setCustomerTaxProfile: builder.mutation<void, { customerId: string; body: TaxProfileBody }>({
      query: ({ customerId, body }) => ({ url: `/users/customers/${customerId}/tax-profile`, method: "PUT", body }),
      invalidatesTags: (_result, error, { customerId }) => (error ? [] : [{ type: "TaxProfile", id: customerId }]),
    }),
    getAllInterestRateSchedules: builder.query<InterestRateSchedule[], void>({
      async queryFn(_arg, _api, _extra, baseQuery) {
        const items: InterestRateSchedule[] = [];
        for (let page = 1; ; page += 1) {
          const result = await baseQuery(`/interest/rate-schedules?pageSize=100&page=${page}`);
          if (result.error) {
            return { error: result.error as FetchBaseQueryError };
          }
          const data = result.data as PagedList<InterestRateSchedule>;
          items.push(...data.items);
          if (items.length >= data.totalCount || data.items.length === 0) {
            return { data: items };
          }
        }
      },
      providesTags: ["InterestRateSchedule"],
    }),
    getInterestRateSchedule: builder.query<InterestRateSchedule, string>({
      query: (id) => `/interest/rate-schedules/${id}`,
      providesTags: (_result, _error, id) => [{ type: "InterestRateSchedule", id }],
    }),
    createInterestRateSchedule: builder.mutation<{ id: string }, CreateInterestRateScheduleBody>({
      query: (body) => ({ url: "/interest/rate-schedules", method: "POST", body }),
      invalidatesTags: (_result, error) => (error ? [] : ["InterestRateSchedule"]),
    }),
    updateInterestRateSchedule: builder.mutation<void, { id: string; body: UpdateInterestRateScheduleBody }>({
      query: ({ id, body }) => ({ url: `/interest/rate-schedules/${id}`, method: "PUT", body }),
      invalidatesTags: (_result, error) => (error ? [] : ["InterestRateSchedule"]),
    }),
    deleteInterestRateSchedule: builder.mutation<void, string>({
      query: (id) => ({ url: `/interest/rate-schedules/${id}`, method: "DELETE" }),
      invalidatesTags: (_result, error) => (error ? [] : ["InterestRateSchedule"]),
    }),
    getInterestRuns: builder.query<PagedList<InterestRun>, { page?: number }>({
      query: ({ page = 1 }) => `/interest/runs?page=${page}`,
    }),
    getInterestRun: builder.query<InterestRun, string>({
      query: (id) => `/interest/runs/${id}`,
    }),
    getInterestAccruals: builder.query<PagedList<InterestAccrual>, { accountId: string; transactionId: string }>({
      query: ({ accountId, transactionId }) =>
        `/accounts/${accountId}/interest-accruals?transactionId=${transactionId}&pageSize=100`,
    }),
  }),
});

export const {
  useGetCustomerTaxProfileQuery,
  useSetCustomerTaxProfileMutation,
  useGetAllInterestRateSchedulesQuery,
  useGetInterestRateScheduleQuery,
  useCreateInterestRateScheduleMutation,
  useUpdateInterestRateScheduleMutation,
  useDeleteInterestRateScheduleMutation,
  useGetInterestRunsQuery,
  useGetInterestRunQuery,
  useGetInterestAccrualsQuery,
} = interestApi;
