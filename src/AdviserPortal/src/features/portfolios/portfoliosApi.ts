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
  }),
});

export const { useGetPortfoliosQuery, useCreatePortfolioMutation } = portfoliosApi;
