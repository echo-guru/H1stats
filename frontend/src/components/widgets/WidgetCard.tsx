import React from 'react'
import type { LucideIcon } from 'lucide-react'
import { cn } from '../../lib/utils'

interface WidgetCardProps {
  title: string
  icon?: LucideIcon
  children: React.ReactNode
  className?: string
  action?: React.ReactNode
}

/** Reusable dashboard widget shell — used across dashboard, reports, and future TV displays */
export function WidgetCard({ title, icon: Icon, children, className, action }: WidgetCardProps) {
  return (
    <div
      className={cn(
        'bg-white rounded-lg border border-brand-primary-border shadow-sm p-4 flex flex-col',
        className
      )}
    >
      <div className="flex items-center justify-between mb-3">
        <div className="flex items-center gap-2">
          {Icon && <Icon className="h-5 w-5 text-brand-primary" />}
          <h3 className="text-sm font-semibold text-brand-primary">{title}</h3>
        </div>
        {action}
      </div>
      <div className="flex-1">{children}</div>
    </div>
  )
}

interface StatWidgetProps {
  label: string
  value: string | number
  sublabel?: string
  trend?: 'up' | 'down' | 'neutral'
}

export function StatWidget({ label, value, sublabel }: StatWidgetProps) {
  return (
    <div className="text-center py-2">
      <p className="text-3xl font-bold text-brand-accent">{value}</p>
      <p className="text-sm font-medium text-brand-primary mt-1">{label}</p>
      {sublabel && <p className="text-xs text-muted-foreground mt-0.5">{sublabel}</p>}
    </div>
  )
}
