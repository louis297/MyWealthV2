import { configureStore } from "@reduxjs/toolkit";
import { hydrateSession, sessionSlice } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

export const store = configureStore({
  reducer: {
    session: sessionSlice.reducer,
    [api.reducerPath]: api.reducer,
  },
  middleware: (getDefault) => getDefault().concat(api.middleware),
});

store.dispatch(hydrateSession());

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
