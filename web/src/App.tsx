import { BrowserRouter, Link, NavLink, Outlet, Route, Routes } from 'react-router'
import { api } from './api/client'
import { AuthGate } from './auth/AuthGate'
import { useAuth } from './auth/useAuth'
import { MiniPlayer } from './components/MiniPlayer'
import { useApi } from './lib/useApi'
import { CreatePage } from './pages/CreatePage'
import { HomePage } from './pages/HomePage'
import { LibraryPage } from './pages/LibraryPage'
import { ListenPage } from './pages/ListenPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { ProfilePage } from './pages/ProfilePage'
import { PlayerProvider } from './player/PlayerContext'

export function App() {
  return (
    <AuthGate>
      <SignedInApp />
    </AuthGate>
  )
}

/** Everything behind sign-in. Unmounts on sign-out, which also stops the audio. */
function SignedInApp() {
  // The player starts at the preferred playback speed; everything else works without preferences.
  const prefs = useApi((signal) => api.getPreferences(signal), [])
  return (
    <PlayerProvider initialRate={prefs.data?.playbackSpeed ?? 1}>
      <BrowserRouter>
        <Routes>
          <Route element={<Shell />}>
            <Route index element={<HomePage />} />
            <Route path="new" element={<CreatePage />} />
            <Route path="library" element={<LibraryPage />} />
            <Route path="episodes/:id" element={<ListenPage />} />
            <Route path="profile" element={<ProfilePage />} />
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </PlayerProvider>
  )
}

function Shell() {
  const { user, signOut } = useAuth()
  return (
    <div className="shell">
      <header className="topbar">
        <Link to="/" className="brand">
          Podcast Generator
        </Link>
        <nav className="nav" aria-label="Main">
          <NavLink to="/library">Library</NavLink>
          <NavLink to="/new">New episode</NavLink>
          <NavLink to="/profile">Profile</NavLink>
        </nav>
        <div className="account">
          <span className="account-name" title={user.email ?? undefined}>
            {user.displayName}
          </span>
          <button type="button" className="button button-quiet" onClick={() => void signOut()}>
            Sign out
          </button>
        </div>
      </header>
      <main id="main">
        <Outlet />
      </main>
      <MiniPlayer />
    </div>
  )
}
