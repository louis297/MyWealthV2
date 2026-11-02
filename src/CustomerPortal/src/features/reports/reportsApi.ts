import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

export type ReportCatalogItem = {
  id: string;
  code: string;
  name: string;
};

export type ReportExecution = {
  id: string;
  reportId: string;
  status: string;
  created: string;
};

export const reportsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getReports: builder.query<ReportCatalogItem[], void>({
      query: () => "/reports?scope=Customer",
      providesTags: ["Report"],
    }),
    requestReport: builder.mutation<{ id: string }, { reportId: string; parameters: Record<string, unknown> }>({
      query: (body) => ({
        url: "/reports/request",
        method: "POST",
        body,
      }),
      invalidatesTags: ["Report"],
    }),
    getExecutions: builder.query<PagedList<ReportExecution>, { page: number; reportId?: string }>({
      query: ({ page, reportId }) => {
        const params = new URLSearchParams();
        params.set("scope", "Customer");
        params.set("page", String(page));
        params.set("pageSize", "20");
        if (reportId) {
          params.set("reportId", reportId);
        }
        return `/reports/executions?${params.toString()}`;
      },
      providesTags: ["Report"],
    }),
    getExecution: builder.query<ReportExecution, string>({
      query: (id) => `/reports/executions/${id}`,
    }),
  }),
});

export const {
  useGetReportsQuery,
  useRequestReportMutation,
  useGetExecutionsQuery,
  useGetExecutionQuery,
} = reportsApi;
