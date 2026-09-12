import { createBrowserRouter } from "react-router";
import { CallbackPage } from "@/features/session/CallbackPage";
import { HomePage } from "@/features/session/HomePage";
import { ShellLayout } from "@/layouts/ShellLayout";

export const router = createBrowserRouter([
  {
    path: "/",
    element: (
      <ShellLayout>
        <HomePage />
      </ShellLayout>
    ),
  },
  {
    path: "/callback",
    element: (
      <ShellLayout>
        <CallbackPage />
      </ShellLayout>
    ),
  },
]);
