import { createSlice, type PayloadAction } from "@reduxjs/toolkit";

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
  currentUser: CurrentUser | null;
};

const initialState: SessionState = {
  currentUser: null,
};

export const sessionSlice = createSlice({
  name: "session",
  initialState,
  reducers: {
    setCurrentUser(state, action: PayloadAction<CurrentUser | null>) {
      state.currentUser = action.payload;
    },
    clearSession(state) {
      state.currentUser = null;
    },
  },
});

export const { setCurrentUser, clearSession } = sessionSlice.actions;
