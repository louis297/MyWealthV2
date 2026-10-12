import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

export type Instrument = {
  id: string;
  tenantId: string;
  symbol: string;
  name: string;
  quoteCurrency: string;
  isActive: boolean;
  rowVersion: string;
};

export const instrumentsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getInstruments: builder.query<PagedList<Instrument>, void>({
      query: () => "/instruments?enabledOnly=true&page=1&pageSize=100",
    }),
  }),
});

export const { useGetInstrumentsQuery } = instrumentsApi;
