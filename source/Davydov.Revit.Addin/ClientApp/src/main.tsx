import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { RevitQueryProvider } from "./revit/RevitQueryProvider";
import "./mocks";
import "./styles.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <RevitQueryProvider>
      <App />
    </RevitQueryProvider>
  </StrictMode>,
);
