import { NavLink, Outlet } from "react-router-dom";
import logoUrl from "../assets/images/aiofoia-logo.png";

const linkBase =
    "px-3 py-2 rounded-md text-sm font-medium transition-colors";
const linkInactive = "text-midnight-700 hover:text-midnight-950 hover:bg-midnight-100";
const linkActive = "bg-midnight-100 text-midnight-950";

function navClass({ isActive }: { isActive: boolean }) {
    return `${linkBase} ${isActive ? linkActive : linkInactive}`;
}

export default function AppLayout() {
    return (
        <div className="min-h-screen bg-midnight-950 text-midnight-100">
            <header className="sticky top-0 z-10 border-b border-midnight-200 bg-white shadow-sm">
                <nav
                    aria-label="Primary"
                    className="mx-auto flex max-w-6xl items-center justify-between px-6 py-2"
                >
                    <NavLink to="/" className="flex items-center gap-2 text-white">
                        <img
                            src={logoUrl}
                            alt="AIO FOIA"
                            className="h-12 w-auto object-contain"
                        />
                    </NavLink>
                    <div className="flex items-center gap-2">
                        <NavLink to="/" end className={navClass}>
                            Dashboard
                        </NavLink>
                        <NavLink to="/pending-review" className={navClass}>
                            Pending Review
                        </NavLink>
                        <NavLink to="/submit" className={navClass}>
                            Submit New Request
                        </NavLink>
                    </div>
                </nav>
            </header>
            <main className="mx-auto max-w-6xl px-6 py-8">
                <Outlet />
            </main>
        </div>
    );
}
