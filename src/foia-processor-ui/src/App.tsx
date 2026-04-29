import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import SubmitRequestPage from "./pages/SubmitRequestPage";
import StatusPage from "./pages/StatusPage";
import ReviewListPage from "./pages/ReviewListPage";
import DocumentReviewPage from "./pages/DocumentReviewPage";

export default function App() {
    return (
        <BrowserRouter>
            <Routes>
                <Route path="/" element={<SubmitRequestPage />} />
                <Route path="/requests/:id" element={<StatusPage />} />
                <Route path="/requests/:id/review" element={<ReviewListPage />} />
                <Route path="/requests/:id/documents/:documentId" element={<DocumentReviewPage />} />
                <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
        </BrowserRouter>
    );
}
