import { Link } from 'react-router'

export function NotFoundPage() {
  return (
    <div className="page page-narrow">
      <h1>Page not found</h1>
      <p className="lede">This address doesn't match any page.</p>
      <Link className="button" to="/library">
        Open the library
      </Link>
    </div>
  )
}
