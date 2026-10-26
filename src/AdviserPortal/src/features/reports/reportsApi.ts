import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

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

export type ReportExecution = {
  id: string;
  reportId: string;
  status: string;
  triggerKind: string;
  created: string;
  parameters: ReportParameters;
  asOf?: string | null;
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
    getReportExecutions: builder.query<
      PagedList<ReportExecution>,
      { scope: ReportScope; page: number; reportId?: string }
    >({
      query: ({ scope, page, reportId }) => {
        const params = new URLSearchParams();
        params.set("scope", scope);
        params.set("page", String(page));
        params.set("pageSize", "20");
        if (reportId) {
          params.set("reportId", reportId);
        }
        return `/reports/executions?${params.toString()}`;
      },
      providesTags: ["Report"],
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
    updateReportSchedule: builder.mutation<
      void,
      {
        id: string;
        reportId: string;
        parameters: ReportParameters;
        cron: string;
        isActive: boolean;
        rowVersion: string;
      }
    >({
      query: ({ id, ...body }) => ({
        url: `/reports/schedules/${id}`,
        method: "PUT",
        body,
      }),
      invalidatesTags: ["Report"],
    }),
    disableReportSchedule: builder.mutation<void, string>({
      query: (id) => ({
        url: `/reports/schedules/${id}/disable`,
        method: "POST",
      }),
      invalidatesTags: ["Report"],
    }),
  }),
});

export const {
  useGetReportsQuery,
  useRequestReportMutation,
  useGetReportExecutionsQuery,
  useGetReportSchedulesQuery,
  useCreateReportScheduleMutation,
  useUpdateReportScheduleMutation,
  useDisableReportScheduleMutation,
} = reportsApi;
