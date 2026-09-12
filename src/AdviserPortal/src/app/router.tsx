import { createBrowserRouter } from "react-router";
import { ShellLayout } from "@/layouts/ShellLayout";

export const router = createBrowserRouter([
  {
    path: "/",
    element: (
      <ShellLayout>
        <p>Adviser Portal</p>
      </ShellLayout>
    ),
  },
]);
