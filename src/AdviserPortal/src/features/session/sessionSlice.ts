import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY } from "@/features/session/oidcStorage";

export type CurrentUser = {
  id: string;
  name: string;
  email: string;
  role: string;
  status: string;
  tenantId: string | null;
  tenantCode: string | null;
  adviserId: string | null;
  rowVersion: string;
};

export type SessionState = {
  accessToken: string | null;
  refreshToken: string | null;
  currentUser: CurrentUser | null;
};

const initialState: SessionState = {
  accessToken: null,
  refreshToken: null,
  currentUser: null,
};

export const sessionSlice = createSlice({
  name: "session",
  initialState,
  reducers: {
    setTokens(
      state,
      action: PayloadAction<{ accessToken: string; refreshToken: string | null }>,
    ) {
      state.accessToken = action.payload.accessToken;
      state.refreshToken = action.payload.refreshToken;
      sessionStorage.setItem(ACCESS_TOKEN_KEY, action.payload.accessToken);
      if (action.payload.refreshToken) {
        sessionStorage.setItem(REFRESH_TOKEN_KEY, action.payload.refreshToken);
      } else {
        sessionStorage.removeItem(REFRESH_TOKEN_KEY);
      }
    },
    hydrateSession(state) {
      state.accessToken = sessionStorage.getItem(ACCESS_TOKEN_KEY);
      state.refreshToken = sessionStorage.getItem(REFRESH_TOKEN_KEY);
    },
    setCurrentUser(state, action: PayloadAction<CurrentUser | null>) {
      state.currentUser = action.payload;
    },
    clearSession(state) {
      state.accessToken = null;
      state.refreshToken = null;
      state.currentUser = null;
      sessionStorage.removeItem(ACCESS_TOKEN_KEY);
      sessionStorage.removeItem(REFRESH_TOKEN_KEY);
    },
  },
});

export const { setTokens, hydrateSession, setCurrentUser, clearSession } = sessionSlice.actions;
