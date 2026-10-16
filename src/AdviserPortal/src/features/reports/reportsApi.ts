import { api } from "@/shared/api/api";

export type ReportScope = "Customer" | "Book";

export type ReportParameters = Record<string, unknown>;

export type ReportSchedule = {
  id: string;
  reportId: string;
  parameters: ReportParameters;
  cron: string;
  isActive: boolean;
  rowVersion: string;
};

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
    requestReport: builder.mutation<{ id: string }, { reportId: string; parameters: ReportParameters }>({
      query: (body) => ({
        url: "/reports/request",
        method: "POST",
        body,
      }),
    }),
    getReportSchedules: builder.query<ReportSchedule[], { scope: ReportScope }>({
      query: ({ scope }) => `/reports/schedules?scope=${encodeURIComponent(scope)}`,
      providesTags: ["Report"],
    }),
    createReportSchedule: builder.mutation<
      { id: string },
      { reportId: string; parameters: ReportParameters; cron: string; isActive: boolean }
    >({
      query: (body) => ({
        url: "/reports/schedules",
        method: "POST",
        body,
      }),
      invalidatesTags: ["Report"],
    }),
  }),
});

export const {
  useGetReportsQuery,
  useRequestReportMutation,
  useGetReportSchedulesQuery,
  useCreateReportScheduleMutation,
} = reportsApi;
