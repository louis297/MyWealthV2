import { Provider } from "react-redux";
import { App } from "@/App";
import { store } from "@/app/store";

export function Providers() {
  return (
    <Provider store={store}>
      <App />
    </Provider>
  );
}
