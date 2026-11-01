import { Provider } from "react-redux";
import { App } from "@/App";
import { store } from "@/app/store";
import { ThemeProvider } from "@/components/theme-provider";

export function Providers() {
  return (
    <ThemeProvider defaultTheme="system" storageKey="customer-portal-theme">
      <Provider store={store}>
        <App />
      </Provider>
    </ThemeProvider>
  );
}
