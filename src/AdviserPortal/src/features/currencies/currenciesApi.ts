import { api } from "@/shared/api/api";

export type Currency = {
  code: string;
  name: string;
  decimalPlaces: number;
  isActive: boolean;
};

export const currenciesApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getCurrencies: builder.query<Currency[], void>({
      query: () => "/currencies?enabledOnly=true",
    }),
  }),
});

export const { useGetCurrenciesQuery } = currenciesApi;
