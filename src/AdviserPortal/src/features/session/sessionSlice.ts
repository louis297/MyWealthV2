import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import type { Tenant } from "@/shared/types/tenant";

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
  tenant: Tenant | null;
};

const initialState: SessionState = {
  currentUser: null,
  tenant: null,
};

export const sessionSlice = createSlice({
  name: "session",
  initialState,
  reducers: {
    setCurrentUser(state, action: PayloadAction<CurrentUser | null>) {
      state.currentUser = action.payload;
    },
    setTenant(state, action: PayloadAction<Tenant | null>) {
      state.tenant = action.payload;
    },
    clearSession(state) {
      state.currentUser = null;
      state.tenant = null;
    },
  },
});

export const { setCurrentUser, setTenant, clearSession } = sessionSlice.actions;
