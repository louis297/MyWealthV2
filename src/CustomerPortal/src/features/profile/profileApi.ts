import { api } from "@/shared/api/api";

/** interest-run R8: the Customer's own tax profile; `updatedBy` is null for a Customer. */
export type MyTaxProfile = {
  taxResidency: string;
  irdNumberHeld: boolean;
  rwtRatePercent: number | null;
  withholding: { kind: string; ratePercent: number; basis: string };
  updatedAt: string | null;
  rowVersion: string | null;
};

export const profileApi = api.injectEndpoints({
  endpoints: (builder) => ({
    updateMe: builder.mutation<void, { name: string; rowVersion: string }>({
      query: (body) => ({
        url: "/users/me",
        method: "PUT",
        body,
      }),
      invalidatesTags: ["Me"],
    }),
    getMyTaxProfile: builder.query<MyTaxProfile, void>({
      query: () => "/users/me/tax-profile",
    }),
    updateMePassword: builder.mutation<void, { currentPassword: string; newPassword: string }>({
      query: (body) => ({
        url: "/users/me/password",
        method: "PUT",
        body,
      }),
    }),
  }),
});

export const { useGetMyTaxProfileQuery, useUpdateMeMutation, useUpdateMePasswordMutation } = profileApi;
