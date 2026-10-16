import { api } from "@/shared/api/api";

export type ReportScope = "Customer" | "Book";

export type ReportCatalogItem = {
  id: string;
  code: string;
  name: string;
  function: string;
  scope: string;
  allowedTriggers: string[];
};

export const reportsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getReports: builder.query<ReportCatalogItem[], { scope: ReportScope }>({
      query: ({ scope }) => `/reports?scope=${encodeURIComponent(scope)}`,
      providesTags: ["Report"],
    }),
    requestReport: builder.mutation<
      { id: string },
      { reportId: string; parameters: Record<string, unknown> }
    >({
      query: (body) => ({
        url: "/reports/request",
        method: "POST",
        body,
      }),
    }),
  }),
});

export const { useGetReportsQuery, useRequestReportMutation } = reportsApi;
