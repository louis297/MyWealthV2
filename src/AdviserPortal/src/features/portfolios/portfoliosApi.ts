import type { FetchBaseQueryError } from "@reduxjs/toolkit/query";
import type { Account } from "@/features/accounts/accountsApi";
import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

export type PortfolioLine = {
  instrumentId: string;
  targetWeight: number;
  sortOrder: number;
};

export type Portfolio = {
  id: string;
  tenantId: string;
  name: string;
  baseCurrency: string;
  isActive: boolean;
  driftAbsolute: number;
  driftRelative: number;
  minimumTrade: number;
  cashResidualPercent: number;
  rowVersion: string;
  lines: PortfolioLine[];
};

export type PortfolioLineInput = {
  instrumentId: string;
  targetWeight: number;
};

export type AccountPortfolio = {
  accountId: string;
  portfolioId: string;
  status: string;
  lastRebalanceAt: string | null;
  lastRebalanceStatus: string | null;
  lastRebalanceMessage: string | null;
};

export type AssignedAccount = {
  account: Account;
  assignment: AccountPortfolio;
};

export type RebalanceAccepted = {
  jobId: string;
  accounts: { accountId: string; handle: string }[];
};

export type UpdatePortfolioBody = {
  rowVersion: string;
  name: string;
  lines: PortfolioLineInput[];
};

export type CreatePortfolioBody = {
  name: string;
  baseCurrency: string;
  lines: PortfolioLineInput[];
  driftAbsolute?: number;
  driftRelative?: number;
  minimumTrade?: number;
  cashResidualPercent?: number;
};

export const portfoliosApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getPortfolios: builder.query<PagedList<Portfolio>, { page?: number } | void>({
      query: (args) => {
        const params = new URLSearchParams();
        params.set("page", String(args?.page ?? 1));
        params.set("pageSize", "20");
        return `/portfolios?${params.toString()}`;
      },
      providesTags: ["Portfolio"],
    }),
    createPortfolio: builder.mutation<{ id: string }, CreatePortfolioBody>({
      query: (body) => ({
        url: "/portfolios",
        method: "POST",
        body,
      }),
      invalidatesTags: ["Portfolio"],
    }),
    getPortfolio: builder.query<Portfolio, string>({
      query: (id) => `/portfolios/${id}`,
      providesTags: (_result, _error, id) => [{ type: "Portfolio", id }],
    }),
    getAssignedAccounts: builder.query<AssignedAccount[], string>({
      async queryFn(portfolioId, _api, _extra, baseQuery) {
        const accounts: Account[] = [];
        for (const type of ["Brokerage", "Other"]) {
          let page = 1;
          let loaded = 0;
          for (;;) {
            const result = await baseQuery(`/accounts?type=${encodeURIComponent(type)}&pageSize=100&page=${page}`);
            if (result.error) {
              return { error: result.error as FetchBaseQueryError };
            }
            const data = result.data as PagedList<Account>;
            accounts.push(...data.items);
            loaded += data.items.length;
            if (loaded >= data.totalCount || data.items.length === 0) {
              break;
            }
            page += 1;
          }
        }

        const assigned: AssignedAccount[] = [];
        for (const account of accounts) {
          const result = await baseQuery(`/accounts/${account.id}/portfolio`);
          if (result.error) {
            if ((result.error as FetchBaseQueryError).status === 404) {
              continue;
            }
            return { error: result.error as FetchBaseQueryError };
          }
          const assignment = result.data as AccountPortfolio;
          if (assignment.portfolioId === portfolioId && assignment.status === "Active") {
            assigned.push({ account, assignment });
          }
        }
        return { data: assigned };
      },
      providesTags: ["Portfolio", "Account"],
    }),
    disablePortfolio: builder.mutation<void, { id: string; rowVersion: string }>({
      query: ({ id, rowVersion }) => ({
        url: `/portfolios/${id}/disable`,
        method: "POST",
        body: { rowVersion },
      }),
      invalidatesTags: (_result, _error, arg) => [{ type: "Portfolio", id: arg.id }, "Portfolio"],
    }),
    updatePortfolio: builder.mutation<RebalanceAccepted | null, { id: string; body: UpdatePortfolioBody }>({
      query: ({ id, body }) => ({
        url: `/portfolios/${id}`,
        method: "PUT",
        body,
      }),
      invalidatesTags: (_result, _error, arg) => [{ type: "Portfolio", id: arg.id }, "Portfolio"],
    }),
  }),
});

export const {
  useGetPortfoliosQuery,
  useCreatePortfolioMutation,
  useGetPortfolioQuery,
  useGetAssignedAccountsQuery,
  useUpdatePortfolioMutation,
  useDisablePortfolioMutation,
} = portfoliosApi;
