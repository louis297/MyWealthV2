import { configureStore } from "@reduxjs/toolkit";
import { hydrateSession, sessionSlice } from "@/features/session/sessionSlice";

export const store = configureStore({
  reducer: {
    session: sessionSlice.reducer,
  },
});

store.dispatch(hydrateSession());

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
