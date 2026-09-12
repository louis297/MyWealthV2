import { configureStore } from "@reduxjs/toolkit";

const shellReducer = () => ({ name: "adviser-portal" as const });

export const store = configureStore({
  reducer: {
    shell: shellReducer,
  },
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
