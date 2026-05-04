import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import AuthBootstrap from "./auth/AuthBootstrap";
import "./app.css";

ReactDOM.createRoot(document.getElementById("root")!).render(
    <React.StrictMode>
        <AuthBootstrap>
            <App />
        </AuthBootstrap>
    </React.StrictMode>
);
