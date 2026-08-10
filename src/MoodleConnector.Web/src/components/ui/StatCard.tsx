import { cn } from '@/lib/utils';
import { LucideIcon } from 'lucide-react';

interface StatCardProps {
  title: string;
  value: number | string;
  subtitle?: string;
  icon?: LucideIcon;
  trend?: {
    value: number;
    label: string;
    positive?: boolean;
  };
  variant?: 'default' | 'warning' | 'danger' | 'success' | 'pending' | 'risk';
  className?: string;
}

export function StatCard({ 
  title, 
  value, 
  subtitle, 
  icon: Icon,
  trend,
  variant = 'default',
  className 
}: StatCardProps) {
  const variantStyles = {
    default: 'bg-card',
    warning: 'bg-card border-l-2 border-l-status-warning',
    danger: 'bg-card border-l-2 border-l-risk-critico',
    success: 'bg-card border-l-2 border-l-status-success',
    pending: 'bg-card border-l-2 border-l-status-pending',
    risk: 'bg-card border-l-2 border-l-risk-risco',
  };

  return (
    <div className={cn(
      'rounded-lg border p-4 shadow-sm',
      variantStyles[variant],
      className
    )}>
      <div className="flex items-start justify-between">
        <div className="space-y-1">
          <p className="text-sm font-medium text-muted-foreground">{title}</p>
          <p className="text-2xl font-bold tracking-tight">{value}</p>
          {subtitle && (
            <p className="text-xs text-muted-foreground">{subtitle}</p>
          )}
          {trend && (
            <p className={cn(
              'text-xs font-medium',
              trend.positive ? 'text-status-success' : 'text-risk-risco'
            )}>
              {trend.positive ? '+' : ''}{trend.value} {trend.label}
            </p>
          )}
        </div>
        {Icon && (
          <div className={cn(
            'rounded-lg p-2',
          )}>
            <Icon className={cn(
              'h-5 w-5',
              variant === 'default' && 'text-muted-foreground',
              variant === 'warning' && 'text-status-warning',
              variant === 'danger' && 'text-risk-critico',
              variant === 'success' && 'text-status-success',
              variant === 'pending' && 'text-status-pending',
              variant === 'risk' && 'text-risk-risco',
            )} />
          </div>
        )}
      </div>
    </div>
  );
}