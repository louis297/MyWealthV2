import { api } from "@/shared/api/api";

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
    updateMePassword: builder.mutation<void, { currentPassword: string; newPassword: string }>({
      query: (body) => ({
        url: "/users/me/password",
        method: "PUT",
        body,
      }),
    }),
  }),
});

export const { useUpdateMeMutation, useUpdateMePasswordMutation } = profileApi;
