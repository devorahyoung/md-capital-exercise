import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import "./App.css";
import StartCrawl from "./pages/StartCrawl";
import History from "./pages/History";
import JobDetails from "./pages/JobDetails";

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<StartCrawl />} />
        <Route path="/jobs" element={<History />} />
        <Route path="/jobs/:id" element={<JobDetails />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
}
