import { Switch, Route } from "wouter";
// import NotFound from "@/pages/not-found";
// import Home from "@/pages/home";
import NotFound from "./pages/not-found";
import Home from "./pages/home";
import * as React from "react"
import { FC, useState, useEffect } from 'react';

// 1) Define the shape of your config
export interface Config {
  VITE_API_URL: string;
}

// 2) Create a context to hold it
export const ConfigContext = React.createContext<Config | null>(null);

function Router() {
  return (
    <Switch>
      <Route path="/" component={Home} />
      <Route component={NotFound} />
    </Switch>
  );
}

// function App() {
//   console.log("App is being served");
//   return (
//     <Router />
//   );
// }

// 4) Fetch it in your App component
const App: FC = () => {
  const [config, setConfig] = useState<Config | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetch("/api/config")
      .then((res) => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return res.json() as Promise<Config>;
      })
      .then((cfg) => {
        setConfig(cfg);
        setLoading(false);
        // console.log("Config after loading:", cfg);
      })
      .catch((err) => {
        console.error("Failed to load config:", err);
        setError(err.message);
        setLoading(false);
      });
  }, []);

  if (loading) return <div>Loading configuration…</div>;
  if (error) return <div>Error loading config: {error}</div>;

  return (
    <ConfigContext.Provider value={config}>
      <Router />
    </ConfigContext.Provider>
  );
};

export default App;
