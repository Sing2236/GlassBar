import { Navigate, Route, Routes } from "react-router-dom";
import { StudioAuthProvider } from "./auth/StudioAuth";
import ExplorePage from "./pages/ExplorePage";
import EditorPage from "./pages/EditorPage";
import AccountPage from "./pages/AccountPage";

export default function StudioApp() {
  return (
    <StudioAuthProvider>
      <Routes>
        <Route index element={<ExplorePage />} />
        <Route path="editor" element={<EditorPage />} />
        <Route path="account" element={<AccountPage />} />
        <Route path="*" element={<Navigate to="/studio" replace />} />
      </Routes>
    </StudioAuthProvider>
  );
}
