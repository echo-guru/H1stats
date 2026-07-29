import { Link } from 'react-router-dom'
import { ChevronLeft } from 'lucide-react'

export function AdminBackLink() {
  return (
    <Link
      to="/admin"
      className="inline-flex items-center gap-1 text-sm text-brand-primary hover:text-brand-accent transition-colors mb-4"
    >
      <ChevronLeft className="h-4 w-4" />
      Back to Administration
    </Link>
  )
}
