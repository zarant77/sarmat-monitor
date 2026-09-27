import React from "react";
import ReactDOM from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import { App } from "./App";
import { AuthProvider } from "./auth";
import { I18nProvider } from "./i18n";
import "./styles.css";

console.info(`Sarmat Monitor v${__APP_VERSION__} (build ${__BUILD_DATE__}) started`);

const queryClient = new QueryClient({ defaultOptions: { queries: { staleTime: 15_000, retry: 1 } } });
ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode><QueryClientProvider client={queryClient}><BrowserRouter><I18nProvider><AuthProvider><App /></AuthProvider></I18nProvider></BrowserRouter></QueryClientProvider></React.StrictMode>
);
