import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import AppLayout from "./components/AppLayout";
import HomePage from "./pages/HomePage";
import PendingReviewPage from "./pages/PendingReviewPage";
import SubmitRequestPage from "./pages/SubmitRequestPage";
import StatusPage from "./pages/StatusPage";
import RequestDetailsPage from "./pages/RequestDetailsPage";
import ReviewListPage from "./pages/ReviewListPage";
import DocumentReviewPage from "./pages/DocumentReviewPage";

export default function App() {
    return (
        <BrowserRouter>
            <Routes>
                <Route element={<AppLayout />}>
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
                    <Route path="*" element={<Navigate to="/" replace />} />
                </Route>
            </Routes>
        </BrowserRouter>
    );
}
