import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { MsalAuthenticationTemplate } from "@azure/msal-react";
import { InteractionType } from "@azure/msal-browser";
import AppLayout from "./components/AppLayout";
import HomePage from "./pages/HomePage";
import PendingReviewPage from "./pages/PendingReviewPage";
import SubmitRequestPage from "./pages/SubmitRequestPage";
import StatusPage from "./pages/StatusPage";
import RequestDetailsPage from "./pages/RequestDetailsPage";
import ReviewListPage from "./pages/ReviewListPage";
import DocumentReviewPage from "./pages/DocumentReviewPage";
import AdminPage from "./pages/AdminPage";
import SystemHealthPage from "./pages/SystemHealthPage";
import { getAuthConfig } from "./auth/msalConfig";

function ProtectedLayout() {
    const { apiScope } = getAuthConfig();
    return (
        <MsalAuthenticationTemplate
            interactionType={InteractionType.Redirect}
            authenticationRequest={{ scopes: [apiScope] }}
        >
            <AppLayout />
        </MsalAuthenticationTemplate>
    );
}

export default function App() {
    return (
        <BrowserRouter>
            <Routes>
                <Route element={<ProtectedLayout />}>
                    <Route path="/" element={<HomePage />} />
                    <Route path="/pending-review" element={<PendingReviewPage />} />
                    <Route path="/submit" element={<SubmitRequestPage />} />
                    <Route path="/requests/:id" element={<RequestDetailsPage />} />
                    <Route path="/requests/:id/status" element={<StatusPage />} />
                    <Route path="/requests/:id/review" element={<ReviewListPage />} />
                    <Route
                        path="/requests/:id/documents/:documentId"
                        element={<DocumentReviewPage />}
                    />
                    <Route path="/admin" element={<AdminPage />} />
                    <Route path="/system-health" element={<SystemHealthPage />} />
                    <Route path="*" element={<Navigate to="/" replace />} />
                </Route>
            </Routes>
        </BrowserRouter>
    );
}
