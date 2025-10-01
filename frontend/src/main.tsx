import { createRoot } from "react-dom/client";
import App from "./App";
import "./index.css";
import { Toaster } from "./components/ui/toaster";
import { QueryClientProvider } from "@tanstack/react-query";
import { queryClient } from "./lib/queryClient";

// Log the environment mode
console.log(`Application running in ${import.meta.env.MODE} mode`);

// console.log(`Application running w/ envars ${import.meta.env}`);

console.log(`Application running w/ envars ${JSON.stringify(import.meta.env, null, 2)}`);


createRoot(document.getElementById("root")!).render(
  <>
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>
    <Toaster />
  </>
);
